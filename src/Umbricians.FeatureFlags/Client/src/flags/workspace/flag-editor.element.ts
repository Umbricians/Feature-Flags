import { css, customElement, html, nothing, property, state, type PropertyValues } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { umbConfirmModal } from "@umbraco-cms/backoffice/modal";
import type { UUIRadioGroupEvent } from "@umbraco-cms/backoffice/external/uui";
import {
  fetchFlag,
  fetchProviderGroups,
  resetFlag,
  saveFlag,
  type FeatureFlag,
  type FlagAudience,
  type ProviderGroup,
} from "../../api/feature-flags.resource.js";
import { FeatureFlagsRequestError } from "../../api/request-error.js";
import { FEATURE_FLAGS_PERMISSION_MANAGE } from "../../permissions/constants.js";
import { observePermission } from "../../permissions/permissions.js";
import { countAudience } from "../display.js";
import { changedText, describeAudience, stateLabel } from "../describe.js";
import { flagListPath } from "../paths.js";

type Mode = "off" | "everyone" | "audience";

const helpTerms: Record<Mode, string> = {
  off: "umbriciansFeatureFlags_offHelp",
  everyone: "umbriciansFeatureFlags_everyoneHelp",
  audience: "umbriciansFeatureFlags_audienceHelp",
};

const emptyAudience = (): FlagAudience => ({ members: [], memberGroups: [], groups: [] });

/**
 * Edits one flag: whether it is on, and who for. A flag that is switched off keeps its audience, so switching it back
 * on restores it. Flags the backoffice can't edit are shown read-only with the reason.
 */
@customElement("umbricians-feature-flag-editor")
export class FeatureFlagEditorElement extends UmbLitElement {
  @property({ attribute: false })
  flagName?: string;

  @state() private _flag?: FeatureFlag;
  @state() private _notFound = false;
  @state() private _loading = true;
  @state() private _providerGroups: Array<ProviderGroup> = [];
  @state() private _mode: Mode = "off";
  @state() private _audience: FlagAudience = emptyAudience();
  @state() private _dirty = false;
  @state() private _saving = false;
  @state() private _canManage = false;

  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor() {
    super();
    observePermission(this, FEATURE_FLAGS_PERMISSION_MANAGE, (permitted) => (this._canManage = permitted));
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
  }

  protected override updated(changed: PropertyValues<this>) {
    if (changed.has("flagName") && this.flagName) this.#load();
  }

  async #load() {
    if (!this.flagName) return;
    this._loading = true;
    try {
      const [flag, providerGroups] = await Promise.all([
        fetchFlag(this.flagName),
        fetchProviderGroups().catch(() => [] as Array<ProviderGroup>),
      ]);
      this._flag = flag;
      this._providerGroups = providerGroups;
      this._audience = flag.audienceDetail ?? emptyAudience();
      this._mode = !flag.flag.enabled ? "off" : flag.audienceDetail ? "audience" : "everyone";
      this._dirty = false;
      this._notFound = false;
    } catch (error) {
      this._notFound = error instanceof FeatureFlagsRequestError && error.status === 404;
      if (!this._notFound) this.#fail(error);
    } finally {
      this._loading = false;
    }
  }

  get #editable() {
    return !!this._flag?.flag.editable && this._canManage;
  }

  /** The audience to save: the chosen one when on for an audience, the one kept while off, or none for everyone. */
  #audienceToSave(): FlagAudience | null {
    if (this._mode === "everyone") return null;
    if (this._mode === "audience") return this._audience;
    const kept = this._audience.members.length + this._audience.memberGroups.length + this._audience.groups.length > 0;
    return kept || this._flag?.audienceDetail ? this._audience : null;
  }

  async #save() {
    if (!this._flag || !this.#editable) return;
    this._saving = true;
    try {
      await saveFlag(this._flag.flag.name, this._mode !== "off", this.#audienceToSave());
      this.#notifications?.peek("positive", {
        data: { message: this.localize.term("umbriciansFeatureFlags_saved", this._flag.flag.name) },
      });
      await this.#load();
    } catch (error) {
      this.#fail(error);
    } finally {
      this._saving = false;
    }
  }

  async #reset() {
    if (!this._flag) return;
    try {
      await umbConfirmModal(this, {
        headline: this.localize.term("umbriciansFeatureFlags_resetHeadline", this._flag.flag.name),
        content: this.localize.term("umbriciansFeatureFlags_resetMessage", stateLabel(this.localize, this._flag.defaultState)),
        color: "warning",
        confirmLabel: this.localize.term("umbriciansFeatureFlags_reset"),
      });
    } catch {
      return;
    }
    this._saving = true;
    try {
      await resetFlag(this._flag.flag.name);
      this.#notifications?.peek("positive", {
        data: { message: this.localize.term("umbriciansFeatureFlags_resetDone", this._flag.flag.name) },
      });
      await this.#load();
    } catch (error) {
      this.#fail(error);
    } finally {
      this._saving = false;
    }
  }

  #fail(error: unknown) {
    this.#notifications?.peek("danger", {
      data: {
        headline: this.localize.term("umbriciansFeatureFlags_changeFailed"),
        message: error instanceof FeatureFlagsRequestError && error.detail ? error.detail : String(error),
      },
    });
  }

  #change(update: Partial<{ mode: Mode; audience: Partial<FlagAudience> }>) {
    if (update.mode) this._mode = update.mode;
    if (update.audience) this._audience = { ...this._audience, ...update.audience };
    this._dirty = true;
  }

  #toggleProviderGroup(id: string, checked: boolean) {
    const groups = checked ? [...new Set([...this._audience.groups, id])] : this._audience.groups.filter((group) => group !== id);
    this.#change({ audience: { groups } });
  }

  override render() {
    return html`<umb-workspace-editor
      headline=${this._flag?.flag.name ?? this.flagName ?? ""}
      .backPath=${flagListPath()}
      ?loading=${this._loading && !this._flag}
      ?notFound=${this._notFound}>
      ${this._flag ? this.#renderContent(this._flag) : nothing}
      ${this._flag ? this.#renderActions(this._flag) : nothing}
    </umb-workspace-editor>`;
  }

  #renderActions(flag: FeatureFlag) {
    return html`
      ${this._dirty ? html`<uui-tag slot="actions" color="warning" look="secondary">${this.localize.term("umbriciansFeatureFlags_unsaved")}</uui-tag>` : nothing}
      <uui-button
        slot="actions"
        look="secondary"
        label=${this.localize.term("umbriciansFeatureFlags_reset")}
        ?disabled=${!flag.flag.hasOverride || !this._canManage || this._saving}
        @click=${this.#reset}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        label=${this.localize.term("umbriciansFeatureFlags_save")}
        .state=${this._saving ? "waiting" : undefined}
        ?disabled=${!this.#editable || !this._dirty || this._saving}
        @click=${this.#save}></uui-button>
    `;
  }

  #renderContent(flag: FeatureFlag) {
    const readOnly = !this.#editable;
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
                @change=${(e: UUIRadioGroupEvent) => this.#change({ mode: e.target.value as Mode })}>
                ${(["off", "everyone", "audience"] as const).map(
                  (mode) => html`<uui-radio value=${mode} label=${stateLabel(this.localize, mode)}>
                    <div class="option">
                      <strong>${stateLabel(this.localize, mode)}</strong>
                      <small>${this.localize.term(helpTerms[mode])}</small>
                    </div>
                  </uui-radio>`,
                )}
              </uui-radio-group>`}
        </uui-box>
        ${this._mode === "audience" ? this.#renderAudience(readOnly) : nothing}
        <uui-box headline=${this.localize.term("umbriciansFeatureFlags_definition")}>
          ${flag.overrideDefinition
            ? html`<p class="hint">${this.localize.term("umbriciansFeatureFlags_definitionHelp")}</p>
                <pre>${flag.overrideDefinition}</pre>`
            : html`<p class="hint">${this.localize.term("umbriciansFeatureFlags_definitionDefault")}</p>`}
        </uui-box>
      </div>
      <div class="side">${this.#renderInfo(flag)}</div>
    </div>`;
  }

  #renderAudience(readOnly: boolean) {
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
          @change=${(e: Event) => this.#change({ audience: { members: [...(e.target as unknown as { selection: Array<string> }).selection] } })}></umb-input-member>
      </umb-property-layout>
      <umb-property-layout
        label=${this.localize.term("umbriciansFeatureFlags_audienceMemberGroups")}
        description=${this.localize.term("umbriciansFeatureFlags_audienceMemberGroupsHelp")}>
        <umb-input-member-group
          slot="editor"
          .selection=${this._audience.memberGroups}
          ?readonly=${readOnly}
          @change=${(e: Event) => this.#change({ audience: { memberGroups: [...(e.target as unknown as { selection: Array<string> }).selection] } })}></umb-input-member-group>
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
                  @change=${(e: Event) => this.#toggleProviderGroup(group.id, (e.target as HTMLInputElement).checked)}>
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
        ${item.group ? html`<dt>${this.localize.term("umbriciansFeatureFlags_group")}</dt><dd>${item.group}</dd>` : nothing}
        <dt>${this.localize.term("umbriciansFeatureFlags_columnChanged")}</dt>
        <dd>${changedText(this.localize, item)}</dd>
      </dl>
      ${item.tags.length ? html`<div class="tags">${item.tags.map((tag) => html`<uui-tag look="placeholder">${tag}</uui-tag>`)}</div>` : nothing}
      ${item.differsFromDefault
        ? html`<p class="warning">${this.localize.term("umbriciansFeatureFlags_differsHelp")}</p>`
        : nothing}
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
        gap: var(--uui-size-space-4);
      }
      .option {
        display: flex;
        flex-direction: column;
      }
      small,
      .hint {
        color: var(--uui-color-text-alt);
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
      pre {
        margin: 0;
        padding: var(--uui-size-space-4);
        background: var(--uui-color-surface-alt);
        border-radius: var(--uui-border-radius);
        overflow-x: auto;
        font-size: 13px;
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

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flag-editor": FeatureFlagEditorElement;
  }
}
