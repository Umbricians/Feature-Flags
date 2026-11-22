import { customElement, html, property, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_COLLECTION_CONTEXT } from "@umbraco-cms/backoffice/collection";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { umbConfirmModal } from "@umbraco-cms/backoffice/modal";
import { resetFlag, setFlagEnabled } from "../../api/feature-flags.resource.js";
import { FeatureFlagsRequestError } from "../../api/request-error.js";
import { FEATURE_FLAGS_PERMISSION_MANAGE } from "../../permissions/constants.js";
import { observePermission } from "../../permissions/permissions.js";
import { FEATURE_FLAGS_SUMMARY_CONTEXT } from "../summary.context.js";
import { quickAction, type FeatureFlagCollectionItem } from "../display.js";
import { describeAudience } from "../describe.js";

/**
 * A flag's quick switch in the list. Switching off happens at once, since turning a broken feature off should be one
 * click; switching on asks first, saying who it will be on for. An undeclared flag gets a Remove button instead.
 */
@customElement("umbricians-feature-flags-switch")
export class FeatureFlagSwitchElement extends UmbLitElement {
  @property({ attribute: false })
  item?: FeatureFlagCollectionItem;

  @state()
  private _canManage = false;

  @state()
  private _busy = false;

  #collection?: typeof UMB_COLLECTION_CONTEXT.TYPE;
  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;
  #summary?: typeof FEATURE_FLAGS_SUMMARY_CONTEXT.TYPE;
  #environment = "";

  constructor() {
    super();
    observePermission(this, FEATURE_FLAGS_PERMISSION_MANAGE, (permitted) => (this._canManage = permitted));
    this.consumeContext(UMB_COLLECTION_CONTEXT, (context) => (this.#collection = context));
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
    this.consumeContext(FEATURE_FLAGS_SUMMARY_CONTEXT, (context) => {
      this.#summary = context;
      this.observe(context?.summary, (summary) => (this.#environment = summary?.environment ?? ""), "umbriciansFeatureFlagsEnvironment");
    });
  }

  async #toggle(event: Event) {
    const item = this.item;
    const toggle = event.target as HTMLInputElement;
    if (!item) return;
    const enabled = toggle.checked;

    if (enabled) {
      try {
        await umbConfirmModal(this, {
          headline: this.localize.term("umbriciansFeatureFlags_confirmOnHeadline", item.name),
          content: this.localize.term(
            "umbriciansFeatureFlags_confirmOnMessage",
            describeAudience(this.localize, item.audience),
            this.#environment,
          ),
          color: "positive",
          confirmLabel: this.localize.term("umbriciansFeatureFlags_turnOn"),
        });
      } catch {
        toggle.checked = false;
        return;
      }
    }

    await this.#run(
      () => setFlagEnabled(item.name, enabled),
      this.localize.term(enabled ? "umbriciansFeatureFlags_turnedOn" : "umbriciansFeatureFlags_turnedOff", item.name, this.#environment),
      () => (toggle.checked = !enabled),
    );
  }

  async #remove() {
    const item = this.item;
    if (!item) return;
    try {
      await umbConfirmModal(this, {
        headline: this.localize.term("umbriciansFeatureFlags_removeHeadline", item.name),
        content: this.localize.term("umbriciansFeatureFlags_removeMessage"),
        color: "danger",
        confirmLabel: this.localize.term("umbriciansFeatureFlags_remove"),
      });
    } catch {
      return;
    }
    await this.#run(() => resetFlag(item.name), this.localize.term("umbriciansFeatureFlags_removed", item.name));
  }

  async #run(action: () => Promise<unknown>, success: string, undo?: () => void) {
    this._busy = true;
    try {
      await action();
      this.#notifications?.peek("positive", { data: { message: success } });
      this.#collection?.loadCollection();
      this.#summary?.load();
    } catch (error) {
      undo?.();
      this.#notifications?.peek("danger", {
        data: {
          headline: this.localize.term("umbriciansFeatureFlags_changeFailed"),
          message: error instanceof FeatureFlagsRequestError && error.detail ? error.detail : String(error),
        },
      });
    } finally {
      this._busy = false;
    }
  }

  override render() {
    const item = this.item;
    if (!item) return html``;
    const action = quickAction(item, this._canManage);
    const reason = action.reason ?? this.localize.term("umbriciansFeatureFlags_needManage");

    if (action.kind === "remove") {
      return html`<uui-button
        look="secondary"
        compact
        title=${action.allowed ? "" : reason}
        ?disabled=${!action.allowed || this._busy}
        label=${this.localize.term("umbriciansFeatureFlags_remove")}
        @click=${this.#remove}></uui-button>`;
    }

    const label = this.localize.term(item.enabled ? "umbriciansFeatureFlags_switchOff" : "umbriciansFeatureFlags_switchOn", item.name);
    return html`<uui-toggle
      aria-label=${label}
      title=${action.allowed ? label : reason}
      .checked=${item.enabled}
      ?disabled=${!action.allowed || this._busy}
      @change=${this.#toggle}></uui-toggle>`;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-switch": FeatureFlagSwitchElement;
  }
}
