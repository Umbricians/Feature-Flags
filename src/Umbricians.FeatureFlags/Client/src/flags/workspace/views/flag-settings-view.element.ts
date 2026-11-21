import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { UUIRadioGroupEvent } from "@umbraco-cms/backoffice/external/uui";
import type { FeatureFlag, FlagAudience, ProviderGroup } from "../../../api/feature-flags.resource.js";
import { countAudience } from "../../display.js";
import { changedText, describeAudience, stateLabel } from "../../describe.js";
import { FEATURE_FLAG_WORKSPACE_CONTEXT } from "../flag-workspace.context-token.js";
import type { FlagEditorMode } from "../flag-workspace.context.js";

const helpTerms: Record<FlagEditorMode, string> = {
  off: "umbriciansFeatureFlags_offHelp",
  everyone: "umbriciansFeatureFlags_everyoneHelp",
  audience: "umbriciansFeatureFlags_audienceHelp",
};

/**
 * The flag editor's Settings tab: whether the flag is on, and who for. A flag that is switched off keeps its audience,
 * so switching it back on restores it. Flags the backoffice can't edit are shown read-only with the reason.
 */
@customElement("umbricians-feature-flag-settings-view")
export class FeatureFlagSettingsViewElement extends UmbLitElement {
  #context?: typeof FEATURE_FLAG_WORKSPACE_CONTEXT.TYPE;

  @state() private _flag?: FeatureFlag;
  @state() private _mode: FlagEditorMode = "off";
  @state() private _audience: FlagAudience = { members: [], memberGroups: [], groups: [] };
  @state() private _providerGroups: Array<ProviderGroup> = [];
  @state() private _canManage = false;

  constructor() {
    super();
    this.consumeContext(FEATURE_FLAG_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      if (!context) return;
      this.observe(context.flag, (flag) => (this._flag = flag), "umbriciansFlag");
      this.observe(context.mode, (mode) => (this._mode = mode), "umbriciansMode");
      this.observe(context.audience, (audience) => (this._audience = audience), "umbriciansAudience");
      this.observe(context.providerGroups, (groups) => (this._providerGroups = groups), "umbriciansProviderGroups");
      this.observe(context.canManage, (canManage) => (this._canManage = canManage), "umbriciansCanManage");
    });
  }

  override render() {
    const flag = this._flag;
    if (!flag) return nothing;
    const readOnly = !(flag.flag.editable && this._canManage);
    return html`<div class="layout">
      <div class="main">
        ${flag.flag.readOnlyReason || !this._canManage
          ? html`<uui-box class="notice" headline=${this.localize.term("umbriciansFeatureFlags_readOnlyFlag")}>
              ${flag.flag.readOnlyReason ?? this.localize.term("umbriciansFeatureFlags_needManage")}
            </uui-box>`
          : nothing}
        <uui-box headline=${this.localize.term("umbriciansFeatureFlags_columnState")}>
          ${flag.flag.state === "custom" && !flag.flag.editable
            ? html`<p>${this.localize.term("umbriciansFeatureFlags_customState")}</p>`
            : html`<uui-radio-group
                  .value=${this._mode}
                  ?disabled=${readOnly}
                  @change=${(e: UUIRadioGroupEvent) => this.#context?.setMode(e.target.value as FlagEditorMode)}>
                  ${(["off", "everyone", "audience"] as const).map(
                    (mode) => html`<uui-radio value=${mode} label=${stateLabel(this.localize, mode)}></uui-radio>`,
                  )}
                </uui-radio-group>
                <p class="hint state-help">${this.localize.term(helpTerms[this._mode])}</p>`}
        </uui-box>
        ${this._mode === "audience" ? this.#renderAudience(readOnly) : nothing}
      </div>
      <div class="side">${this.#renderInfo(flag)}</div>
    </div>`;
  }

  #renderAudience(readOnly: boolean) {
    const selection = (e: Event) => [...(e.target as unknown as { selection: Array<string> }).selection];
    return html`<uui-box headline=${this.localize.term("umbriciansFeatureFlags_audience")}>
      <p class="summary">
        ${this.localize.term("umbriciansFeatureFlags_onFor", describeAudience(this.localize, countAudience(this._audience)))}
      </p>
      <umb-property-layout
        label=${this.localize.term("umbriciansFeatureFlags_audienceMembers")}
        description=${this.localize.term("umbriciansFeatureFlags_audienceMembersHelp")}>
        <umb-input-member
          slot="editor"
          .selection=${this._audience.members}
          ?readonly=${readOnly}
          @change=${(e: Event) => this.#context?.updateAudience({ members: selection(e) })}></umb-input-member>
      </umb-property-layout>
      <umb-property-layout
        label=${this.localize.term("umbriciansFeatureFlags_audienceMemberGroups")}
        description=${this.localize.term("umbriciansFeatureFlags_audienceMemberGroupsHelp")}>
        <umb-input-member-group
          slot="editor"
          .selection=${this._audience.memberGroups}
          ?readonly=${readOnly}
          @change=${(e: Event) => this.#context?.updateAudience({ memberGroups: selection(e) })}></umb-input-member-group>
      </umb-property-layout>
      ${this._providerGroups.length
        ? html`<umb-property-layout
            label=${this.localize.term("umbriciansFeatureFlags_audienceGroups")}
            description=${this.localize.term("umbriciansFeatureFlags_audienceGroupsHelp")}>
            <div slot="editor" class="checkboxes">
              ${this._providerGroups.map(
                (group) => html`<uui-checkbox
                  label=${group.name}
                  ?checked=${this._audience.groups.includes(group.id)}
                  ?disabled=${readOnly}
                  @change=${(e: Event) => this.#context?.toggleProviderGroup(group.id, (e.target as HTMLInputElement).checked)}>
                  ${group.name}${group.description ? html` <small>${group.description}</small>` : nothing}
                </uui-checkbox>`,
              )}
            </div>
          </umb-property-layout>`
        : nothing}
    </uui-box>`;
  }

  #renderInfo(flag: FeatureFlag) {
    const item = flag.flag;
    const declaredIn = this.localize.term(
      item.declaredIn === "code"
        ? "umbriciansFeatureFlags_declaredInCode"
        : item.declaredIn === "configuration"
          ? "umbriciansFeatureFlags_declaredInConfiguration"
          : "umbriciansFeatureFlags_declaredInFlagSource",
    );
    return html`<uui-box headline=${this.localize.term("umbriciansFeatureFlags_info")}>
      ${item.description ? html`<p>${item.description}</p>` : nothing}
      <dl>
        <dt>${this.localize.term("umbriciansFeatureFlags_default")}</dt>
        <dd>${stateLabel(this.localize, flag.defaultState)}</dd>
        <dt>${this.localize.term("umbriciansFeatureFlags_declaredIn")}</dt>
        <dd>${declaredIn}</dd>
        ${item.section ? html`<dt>${this.localize.term("umbriciansFeatureFlags_section")}</dt><dd>${item.section}</dd>` : nothing}
        <dt>${this.localize.term("umbriciansFeatureFlags_columnChanged")}</dt>
        <dd>${changedText(this.localize, item)}</dd>
      </dl>
      ${item.tags.length ? html`<div class="tags">${item.tags.map((tag) => html`<uui-tag look="placeholder">${tag}</uui-tag>`)}</div>` : nothing}
      ${item.differsFromDefault ? html`<p class="warning">${this.localize.term("umbriciansFeatureFlags_differsHelp")}</p>` : nothing}
      ${item.declaredTwice ? html`<p class="warning">${this.localize.term("umbriciansFeatureFlags_declaredTwiceHelp")}</p>` : nothing}
    </uui-box>`;
  }

  static override styles = [
    UmbTextStyles,
    css`
      .layout {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-layout-1);
        padding: var(--uui-size-layout-1);
        align-items: flex-start;
      }
      .main {
        flex: 999 1 520px;
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-layout-1);
      }
      .side {
        flex: 1 1 280px;
        max-width: 400px;
      }
      uui-radio-group {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
      }
      small,
      .hint {
        color: var(--uui-color-text-alt);
      }
      .state-help {
        margin-bottom: 0;
      }
      .summary {
        font-weight: bold;
        margin-top: 0;
      }
      .checkboxes {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
      }
      dl {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--uui-size-space-2) var(--uui-size-space-4);
        margin: 0;
      }
      dt {
        color: var(--uui-color-text-alt);
      }
      dd {
        margin: 0;
      }
      .tags {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-1);
        margin-top: var(--uui-size-space-4);
      }
      .warning {
        color: var(--uui-color-warning-standalone);
        margin-bottom: 0;
      }
      .notice {
        --uui-box-default-padding: var(--uui-size-space-4);
      }
    `,
  ];
}

export { FeatureFlagSettingsViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flag-settings-view": FeatureFlagSettingsViewElement;
  }
}
