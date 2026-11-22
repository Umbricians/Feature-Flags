import { css, customElement, html, nothing, property, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";
import type { AudienceGroup, FlagAudience, ProviderGroup } from "../../api/feature-flags.resource.js";
import {
  audienceFromParameters,
  countAudience,
  groupsOfKind,
  hasGroup,
  memberGroupKeys,
  toTargetingCondition,
  withMemberGroupKeys,
  withoutGroup,
  withProviderGroup,
} from "../../flags/audience.js";
import { describeAudience } from "../../flags/describe.js";
import { FEATURE_FLAG_WORKSPACE_CONTEXT } from "../../flags/workspace/flag-workspace.context-token.js";
import type { UmbriciansFeatureFlagsConditionElement } from "../condition.extension.js";

/**
 * The editor of the audience condition (`Microsoft.Targeting`): members, member groups and groups from group providers.
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
      this.observe(context.flag, (flag) => (this._known = flag?.audienceDetail?.groups ?? []), "umbriciansAudienceKnown");
      this.observe(context.providerGroups, (groups) => (this._providerGroups = groups), "umbriciansAudienceProviderGroups");
    });
  }

  get #audience(): FlagAudience {
    const providerGroups = this._providerGroups.map((group): AudienceGroup => ({ id: group.id, kind: "providerGroup", name: group.name }));
    return audienceFromParameters(this.value, [...this._known, ...providerGroups]);
  }

  #change(audience: FlagAudience) {
    this.value = toTargetingCondition(audience).parameters;
    this.dispatchEvent(new UmbChangeEvent());
  }

  override render() {
    const audience = this.#audience;
    const selection = (e: Event) => [...(e.target as unknown as { selection: Array<string> }).selection];
    const named = groupsOfKind(audience, "namedMemberGroup");
    const unknown = groupsOfKind(audience, "unknown");
    // The pickers fire their own change events, which stop here: the flag editor hears only this element's.
    const picked = (e: Event, apply: (audience: FlagAudience) => FlagAudience) => {
      e.stopPropagation();
      this.#change(apply(this.#audience));
    };
    return html`<p class="summary">
        ${this.localize.term("umbriciansFeatureFlags_onFor", describeAudience(this.localize, countAudience(audience)))}
      </p>
      <umb-property-layout
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
            this.#renderKeptGroup(group, this.localize.term("umbriciansFeatureFlags_namedMemberGroup", group.name ?? group.id)),
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
                      this.#change(withProviderGroup(this.#audience, group, (e.target as HTMLInputElement).checked));
                    }}>
                    ${group.name}${group.description ? html` <small>${group.description}</small>` : nothing}
                  </uui-checkbox>`,
                )}
              </div>
              ${unknown.map((group) => this.#renderKeptGroup(group, this.localize.term("umbriciansFeatureFlags_unknownGroup", group.id)))}
              ${unknown.length ? html`<small>${this.localize.term("umbriciansFeatureFlags_unknownGroupsHelp")}</small>` : nothing}
            </div>
          </umb-property-layout>`
        : nothing}`;
  }

  /** A group the pickers can't show, as a tag that stays in the audience until it is removed. */
  #renderKeptGroup(group: AudienceGroup, label: string) {
    return html`<div class="kept">
      <uui-tag look="secondary">${label}</uui-tag>
      ${this.readonly
        ? nothing
        : html`<uui-button
            compact
            look="secondary"
            label=${this.localize.term("umbriciansFeatureFlags_removeGroup", group.name ?? group.id)}
            @click=${() => this.#change(withoutGroup(this.#audience, group.id))}>
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
