import { css, customElement, html, nothing, repeat, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { UUIPopoverContainerElement, UUIRadioGroupEvent } from "@umbraco-cms/backoffice/external/uui";
import type { ConditionMatch, FeatureFlag, FlagCondition, RegisteredCondition } from "../../../api/feature-flags.resource.js";
import { displayNameOf, hasEditor, namesMatch } from "../../../conditions/condition-names.js";
import { mayDo } from "../../display.js";
import { changedText, stateLabel } from "../../describe.js";
import { FEATURE_FLAG_WORKSPACE_CONTEXT } from "../flag-workspace.context-token.js";
import { isCustom, type FlagEditorMode, type SaveBlocker } from "../flag-editor-state.js";

const helpTerms: Record<FlagEditorMode, string> = {
  off: "umbriciansFeatureFlags_offHelp",
  everyone: "umbriciansFeatureFlags_everyoneHelp",
  conditions: "umbriciansFeatureFlags_conditionsHelp",
};

/**
 * The flag editor's Settings tab: whether the flag is on, and under what conditions. A flag that is switched off keeps its
 * conditions, so switching it back on restores them. Each condition is edited by the element of the manifest whose
 * `meta.name` matches it. Flags the backoffice can't edit are shown read-only with the reason.
 */
@customElement("umbricians-feature-flags-settings-view")
export class FeatureFlagSettingsViewElement extends UmbLitElement {
  #context?: typeof FEATURE_FLAG_WORKSPACE_CONTEXT.TYPE;
  // One filter function for each condition name: the extension slot starts over whenever it is given a new one.
  #filters = new Map<string, (manifest: { meta: { name: string } }) => boolean>();

  @state() private _flag?: FeatureFlag;
  @state() private _mode: FlagEditorMode = "off";
  @state() private _conditions: ReadonlyArray<FlagCondition> = [];
  @state() private _match: ConditionMatch = "any";
  @state() private _switchedForRoot = false;
  @state() private _registered: Array<RegisteredCondition> = [];
  @state() private _addable: Array<RegisteredCondition> = [];
  @state() private _editorNames: Array<string> = [];
  @state() private _blocker: SaveBlocker | null = null;
  @state() private _canManage = false;

  constructor() {
    super();
    this.consumeContext(FEATURE_FLAG_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      if (!context) return;
      this.observe(context.flag, (flag) => (this._flag = flag), "umbriciansFlag");
      this.observe(context.mode, (mode) => (this._mode = mode), "umbriciansMode");
      this.observe(context.conditions, (conditions) => (this._conditions = conditions), "umbriciansConditions");
      this.observe(context.match, (match) => (this._match = match), "umbriciansMatch");
      this.observe(context.matchSwitchedForRoot, (switched) => (this._switchedForRoot = switched), "umbriciansMatchSwitched");
      this.observe(context.registered, (registered) => (this._registered = registered), "umbriciansRegistered");
      this.observe(context.addable, (addable) => (this._addable = addable), "umbriciansAddable");
      this.observe(context.editorNames, (names) => (this._editorNames = names), "umbriciansEditorNames");
      this.observe(context.saveBlocker, (blocker) => (this._blocker = blocker), "umbriciansSaveBlocker");
      this.observe(context.canManage, (canManage) => (this._canManage = canManage), "umbriciansCanManage");
    });
  }

  override render() {
    const flag = this._flag;
    if (!flag) return nothing;
    const save = mayDo(flag, "save", this._canManage);
    const readOnly = !save.allowed;
    return html`<div class="layout">
      <div class="main">
        ${!save.allowed
          ? html`<uui-box class="notice" headline=${this.localize.term("umbriciansFeatureFlags_readOnlyFlag")}>
              ${save.reason ?? this.localize.term("umbriciansFeatureFlags_needManage")}
            </uui-box>`
          : nothing}
        <uui-box headline=${this.localize.term("umbriciansFeatureFlags_columnState")}>
          ${isCustom(flag)
            ? html`<p>${this.localize.term("umbriciansFeatureFlags_customState")}</p>`
            : html`<uui-radio-group
                  .value=${this._mode}
                  ?disabled=${readOnly}
                  @change=${(e: UUIRadioGroupEvent) => this.#context?.setMode(e.target.value as FlagEditorMode)}>
                  ${(["off", "everyone", "conditions"] as const).map(
                    (mode) => html`<uui-radio value=${mode} label=${stateLabel(this.localize, mode)}></uui-radio>`,
                  )}
                </uui-radio-group>
                <p class="hint state-help">${this.localize.term(helpTerms[this._mode])}</p>`}
        </uui-box>
        ${this._mode === "conditions" && !isCustom(flag) ? this.#renderConditions(flag, readOnly) : nothing}
      </div>
      <div class="side">${this.#renderInfo(flag)}</div>
    </div>`;
  }

  #renderConditions(flag: FeatureFlag, readOnly: boolean) {
    const conditions = this._conditions;
    const withoutEditor = conditions.some((condition) => !hasEditor(this._editorNames, condition.name));
    return html`${this._blocker ? html`<uui-box class="notice warning">${this.#blockerText(this._blocker)}</uui-box>` : nothing}
      ${withoutEditor && flag.summary
        ? html`<p class="hint">${this.localize.term("umbriciansFeatureFlags_currently", flag.summary)}</p>`
        : nothing}
      ${conditions.length === 0 ? html`<p class="hint">${this.localize.term("umbriciansFeatureFlags_noConditions")}</p>` : nothing}
      ${repeat(
        conditions,
        (condition) => condition.name,
        (condition, index) => this.#renderCondition(condition, index, readOnly),
      )}
      ${conditions.length > 1 ? this.#renderMatch(readOnly) : nothing}
      ${readOnly ? nothing : this.#renderAdd()}`;
  }

  #renderCondition(condition: FlagCondition, index: number, readOnly: boolean) {
    const name = displayNameOf(this._registered, condition);
    return html`<uui-box headline=${name}>
      ${readOnly
        ? nothing
        : html`<uui-button
            slot="header-actions"
            compact
            look="secondary"
            label=${this.localize.term("umbriciansFeatureFlags_removeCondition", name)}
            @click=${() => this.#context?.removeCondition(index)}>
            <uui-icon name="icon-trash"></uui-icon>
          </uui-button>`}
      ${hasEditor(this._editorNames, condition.name)
        ? html`<umb-extension-slot
            type="umbriciansFeatureFlagsCondition"
            .filter=${this.#filterFor(condition.name)}
            .props=${{ value: condition.parameters, readonly: readOnly }}
            .events=${{
              change: (event: Event) =>
                this.#context?.setConditionParameters(index, (event.target as unknown as { value: Record<string, unknown> }).value),
            }}></umb-extension-slot>`
        : html`<p class="warning">${this.localize.term("umbriciansFeatureFlags_noEditor")}</p>`}
    </uui-box>`;
  }

  #filterFor(name: string) {
    let filter = this.#filters.get(name);
    if (!filter) {
      filter = (manifest) => namesMatch(manifest.meta.name, name);
      this.#filters.set(name, filter);
    }
    return filter;
  }

  #renderMatch(readOnly: boolean) {
    return html`<uui-box headline=${this.localize.term("umbriciansFeatureFlags_match")}>
      <uui-radio-group
        .value=${this._match}
        ?disabled=${readOnly}
        @change=${(e: UUIRadioGroupEvent) => this.#context?.setMatch(e.target.value as ConditionMatch)}>
        <uui-radio value="any" label=${this.localize.term("umbriciansFeatureFlags_matchAny")}></uui-radio>
        <uui-radio value="all" label=${this.localize.term("umbriciansFeatureFlags_matchAll")}></uui-radio>
      </uui-radio-group>
      ${this._switchedForRoot ? html`<p class="hint">${this.localize.term("umbriciansFeatureFlags_matchSwitchedForRoot")}</p>` : nothing}
    </uui-box>`;
  }

  #renderAdd() {
    const add = (name: string, e: Event) => {
      (e.target as HTMLElement).closest<UUIPopoverContainerElement>("uui-popover-container")?.hidePopover();
      this.#context?.addCondition(name);
    };
    return html`<div>
      <uui-button
        look="placeholder"
        popovertarget="add-condition"
        label=${this.localize.term("umbriciansFeatureFlags_addCondition")}></uui-button>
      <uui-popover-container id="add-condition" placement="bottom-start">
        <umb-popover-layout>
          ${this._addable.length === 0
            ? html`<p class="hint menu-empty">${this.localize.term("umbriciansFeatureFlags_noMoreConditions")}</p>`
            : this._addable.map(
                (condition) => html`<uui-menu-item label=${condition.displayName} @click=${(e: Event) => add(condition.name, e)}></uui-menu-item>`,
              )}
        </umb-popover-layout>
      </uui-popover-container>
    </div>`;
  }

  #blockerText(blocker: SaveBlocker) {
    return blocker.kind === "noEditor"
      ? this.localize.term("umbriciansFeatureFlags_cannotSaveNoEditor", blocker.names.join(", "))
      : this.localize.term("umbriciansFeatureFlags_cannotSaveNoConditions");
  }

  #renderInfo(flag: FeatureFlag) {
    const item = flag;
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
      .menu-empty {
        margin: var(--uui-size-space-4);
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
      .notice.warning {
        color: var(--uui-color-warning-standalone);
      }
    `,
  ];
}

export { FeatureFlagSettingsViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-settings-view": FeatureFlagSettingsViewElement;
  }
}
