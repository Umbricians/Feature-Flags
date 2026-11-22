import { css, customElement, html, live, nothing, property, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";
import type { AudienceGroup, FlagAudience, ProviderGroup } from "../../api/feature-flags.resource.js";
import {
  audienceFromParameters,
  countAudience,
  groupsOfKind,
  hasGroup,
  memberGroupKeys,
  sideOf,
  toTargetingCondition,
  withDefaultPercentage,
  withGroupPercentage,
  withMemberGroupKeys,
  withoutGroup,
  withProviderGroup,
  withSide,
  type AudienceSide,
} from "../../flags/audience.js";
import { describeAudience } from "../../flags/describe.js";
import { FEATURE_FLAG_WORKSPACE_CONTEXT } from "../../flags/workspace/flag-workspace.context-token.js";
import type { UmbriciansFeatureFlagsConditionElement } from "../condition.extension.js";

/**
 * The editor of the audience condition (`Microsoft.Targeting`): members, member groups and groups from group providers, with a percentage of everyone else and of each group, and
 * the members and groups left out.
 * It takes the condition's parameters in `value` and, when an editor changes them, sets `value` and fires `change`. What
 * each group is, and the groups the site's providers offer, come from the flag editor's workspace.
 */
@customElement("umbricians-feature-flags-audience-condition")
export class AudienceConditionElement extends UmbLitElement implements UmbriciansFeatureFlagsConditionElement {
  /** The condition's parameters, as Microsoft.Targeting reads them. */
  @property({ attribute: false })
  value: Record<string, unknown> = {};

  @property({ type: Boolean, reflect: true })
  readonly = false;

  @state() private _known: Array<AudienceGroup> = [];
  @state() private _providerGroups: Array<ProviderGroup> = [];

  constructor() {
    super();
    this.consumeContext(FEATURE_FLAG_WORKSPACE_CONTEXT, (context) => {
      if (!context) return;
      this.observe(
        context.flag,
        (flag) => (this._known = [...(flag?.audienceDetail?.groups ?? []), ...(flag?.audienceDetail?.excludedGroups ?? [])]),
        "umbriciansAudienceKnown",
      );
      this.observe(context.providerGroups, (groups) => (this._providerGroups = groups), "umbriciansAudienceProviderGroups");
    });
  }

  get #audience(): FlagAudience {
    const providerGroups = this._providerGroups.map((group): AudienceGroup => ({ id: group.id, kind: "providerGroup", name: group.name, percentage: 100 }));
    return audienceFromParameters(this.value, [...this._known, ...providerGroups]);
  }

  #change(audience: FlagAudience) {
    this.value = toTargetingCondition(audience).parameters;
    this.dispatchEvent(new UmbChangeEvent());
  }

  override render() {
    const audience = this.#audience;
    return html`<p class="summary">
        ${this.localize.term("umbriciansFeatureFlags_onFor", describeAudience(this.localize, countAudience(audience)))}
      </p>
      ${this.#renderPickers(audience, "included")}
      <umb-property-layout
        label=${this.localize.term("umbriciansFeatureFlags_audienceDefaultPercentage")}
        description=${this.localize.term("umbriciansFeatureFlags_audienceDefaultPercentageHelp")}>
        <div slot="editor">${this.#renderPercentage(audience.defaultPercentage, "umbriciansFeatureFlags_percentOfEveryoneElseUnit", this.localize.term("umbriciansFeatureFlags_audienceDefaultPercentage"), (value) => withDefaultPercentage(this.#audience, value))}</div>
      </umb-property-layout>
      ${audience.groups.length
        ? html`<umb-property-layout
            label=${this.localize.term("umbriciansFeatureFlags_audienceGroupPercentages")}
            description=${this.localize.term("umbriciansFeatureFlags_audienceGroupPercentagesHelp")}>
            <div slot="editor" class="groups">
              ${audience.groups.map((group) =>
                this.#renderPercentage(
                  group.percentage,
                  "umbriciansFeatureFlags_percentOfThisGroup",
                  this.localize.term("umbriciansFeatureFlags_groupPercentage", group.name ?? group.id),
                  (value) => withGroupPercentage(this.#audience, group.id, value),
                  group.name ?? group.id,
                ),
              )}
            </div>
          </umb-property-layout>`
        : nothing}
      <uui-box headline=${this.localize.term("umbriciansFeatureFlags_audienceExcluded")}>
        <p class="help">${this.localize.term("umbriciansFeatureFlags_audienceExcludedHelp")}</p>
        ${this.#renderPickers(audience, "excluded")}
      </uui-box>`;
  }

  /** A whole number from 0 to 100 and what it is a percentage of. Typing something that isn't a number changes nothing. */
  #renderPercentage(value: number, unitTerm: string, label: string, apply: (value: string) => FlagAudience, name?: string) {
    return html`<div class="percentage">
      ${name ? html`<span>${name}</span>` : nothing}
      <uui-input
        type="number"
        min="0"
        max="100"
        step="1"
        label=${label}
        .value=${live(String(value))}
        ?readonly=${this.readonly}
        @change=${(e: Event) => {
          e.stopPropagation();
          this.#change(apply((e.target as HTMLInputElement).value));
        }}></uui-input>
      <span>${this.localize.term(unitTerm as "umbriciansFeatureFlags_percentOfThisGroup")}</span>
    </div>`;
  }

  /** The members, member groups and other groups pickers of one side of the audience: who is in it, or who is left out. */
  #renderPickers(whole: FlagAudience, side: AudienceSide) {
    const audience = sideOf(whole, side);
    const selection = (e: Event) => [...(e.target as unknown as { selection: Array<string> }).selection];
    // A group named by name that matches a member group shows in the picker like any other, so only one that doesn't is a tag.
    const named = groupsOfKind(audience, "namedMemberGroup").filter((group) => !group.memberGroupKey);
    const unknown = groupsOfKind(audience, "unknown");
    // The pickers fire their own change events, which stop here: the flag editor hears only this element's.
    const picked = (e: Event, apply: (audience: FlagAudience) => FlagAudience) => {
      e.stopPropagation();
      this.#change(withSide(this.#audience, side, apply(sideOf(this.#audience, side))));
    };
    const edit = (apply: (audience: FlagAudience) => FlagAudience) => this.#change(withSide(this.#audience, side, apply(sideOf(this.#audience, side))));
    return html`<umb-property-layout
        label=${this.localize.term("umbriciansFeatureFlags_audienceMembers")}
        description=${this.localize.term("umbriciansFeatureFlags_audienceMembersHelp")}>
        <umb-input-member
          slot="editor"
          .selection=${audience.members}
          ?readonly=${this.readonly}
          @change=${(e: Event) => picked(e, (current) => ({ ...current, members: selection(e) }))}></umb-input-member>
      </umb-property-layout>
      <umb-property-layout
        label=${this.localize.term("umbriciansFeatureFlags_audienceMemberGroups")}
        description=${this.localize.term("umbriciansFeatureFlags_audienceMemberGroupsHelp")}>
        <div slot="editor" class="groups">
          <umb-input-member-group
            .selection=${memberGroupKeys(audience)}
            ?readonly=${this.readonly}
            @change=${(e: Event) => picked(e, (current) => withMemberGroupKeys(current, selection(e)))}></umb-input-member-group>
          ${named.map((group) =>
            this.#renderKeptGroup(group, this.localize.term("umbriciansFeatureFlags_namedMemberGroup", group.name ?? group.id), edit),
          )}
        </div>
      </umb-property-layout>
      ${this._providerGroups.length || unknown.length
        ? html`<umb-property-layout
            label=${this.localize.term("umbriciansFeatureFlags_audienceGroups")}
            description=${this.localize.term("umbriciansFeatureFlags_audienceGroupsHelp")}>
            <div slot="editor" class="groups">
              <div class="checkboxes">
                ${this._providerGroups.map(
                  (group) => html`<uui-checkbox
                    label=${group.name}
                    ?checked=${hasGroup(audience, group.id)}
                    ?disabled=${this.readonly}
                    @change=${(e: Event) => {
                      e.stopPropagation();
                      edit((current) => withProviderGroup(current, group, (e.target as HTMLInputElement).checked));
                    }}>
                    ${group.name}${group.description ? html` <small>${group.description}</small>` : nothing}
                  </uui-checkbox>`,
                )}
              </div>
              ${unknown.map((group) => this.#renderKeptGroup(group, this.localize.term("umbriciansFeatureFlags_unknownGroup", group.id), edit))}
              ${unknown.length ? html`<small>${this.localize.term("umbriciansFeatureFlags_unknownGroupsHelp")}</small>` : nothing}
            </div>
          </umb-property-layout>`
        : nothing}`;
  }

  /** A group the pickers can't show, as a tag that stays in the audience until it is removed. */
  #renderKeptGroup(group: AudienceGroup, label: string, edit: (apply: (audience: FlagAudience) => FlagAudience) => void) {
    return html`<div class="kept">
      <uui-tag look="secondary">${label}</uui-tag>
      ${this.readonly
        ? nothing
        : html`<uui-button
            compact
            look="secondary"
            label=${this.localize.term("umbriciansFeatureFlags_removeGroup", group.name ?? group.id)}
            @click=${() => edit((current) => withoutGroup(current, group.id))}>
            <uui-icon name="icon-wrong"></uui-icon>
          </uui-button>`}
    </div>`;
  }

  static override styles = css`
    :host {
      display: block;
    }
    small {
      color: var(--uui-color-text-alt);
    }
    .summary {
      font-weight: bold;
      margin-top: 0;
    }
    .groups,
    .checkboxes {
      display: flex;
      flex-direction: column;
      gap: var(--uui-size-space-2);
    }
    .help {
      margin-top: 0;
    }
    .percentage {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
    }
    uui-input[type="number"] {
      width: 6em;
    }
    .kept {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
    }
  `;
}

export { AudienceConditionElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-audience-condition": AudienceConditionElement;
  }
}
