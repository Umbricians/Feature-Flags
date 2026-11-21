import { customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import type { FeatureFlag } from "../../api/feature-flags.resource.js";
import { mayDo } from "../display.js";
import { flagListPath } from "../paths.js";
import { FEATURE_FLAG_WORKSPACE_CONTEXT } from "./flag-workspace.context-token.js";

/**
 * The flag editor's frame: the flag's name, its tabs (Settings, Definition, registered as workspace views) and the
 * Save and Reset buttons, which act on the workspace's shared state whichever tab is open.
 */
@customElement("umbricians-feature-flags-editor")
export class FeatureFlagEditorElement extends UmbLitElement {
  #context?: typeof FEATURE_FLAG_WORKSPACE_CONTEXT.TYPE;

  @state() private _flag?: FeatureFlag;
  @state() private _loading = true;
  @state() private _notFound = false;
  @state() private _dirty = false;
  @state() private _saving = false;
  @state() private _canManage = false;

  constructor() {
    super();
    this.consumeContext(FEATURE_FLAG_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      if (!context) return;
      this.observe(context.flag, (flag) => (this._flag = flag), "umbriciansFlag");
      this.observe(context.loading, (loading) => (this._loading = loading), "umbriciansLoading");
      this.observe(context.notFound, (notFound) => (this._notFound = notFound), "umbriciansNotFound");
      this.observe(context.dirty, (dirty) => (this._dirty = dirty), "umbriciansDirty");
      this.observe(context.saving, (saving) => (this._saving = saving), "umbriciansSaving");
      this.observe(context.canManage, (canManage) => (this._canManage = canManage), "umbriciansCanManage");
    });
  }

  override render() {
    const flag = this._flag;
    const item = flag?.flag;
    const undo = item?.isDeclared === false ? "remove" : "reset";
    const canUndo = !!item && mayDo(item, undo, this._canManage).allowed;
    const canSave = !!item && mayDo(item, "save", this._canManage).allowed;
    return html`<umb-workspace-editor
      headline=${flag?.flag.name ?? ""}
      .backPath=${flagListPath()}
      ?loading=${this._loading && !flag}
      ?notFound=${this._notFound}>
      ${this._dirty
        ? html`<uui-tag slot="actions" color="warning" look="secondary">${this.localize.term("umbriciansFeatureFlags_unsaved")}</uui-tag>`
        : nothing}
      <uui-button
        slot="actions"
        look="secondary"
        label=${this.localize.term(undo === "remove" ? "umbriciansFeatureFlags_remove" : "umbriciansFeatureFlags_reset")}
        ?disabled=${!canUndo || this._saving}
        @click=${() => this.#context?.reset()}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        label=${this.localize.term("umbriciansFeatureFlags_save")}
        .state=${this._saving ? "waiting" : undefined}
        ?disabled=${!canSave || !this._dirty || this._saving}
        @click=${() => this.#context?.save()}></uui-button>
    </umb-workspace-editor>`;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-editor": FeatureFlagEditorElement;
  }
}
