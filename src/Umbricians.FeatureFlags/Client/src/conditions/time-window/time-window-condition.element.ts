import { css, customElement, html, nothing, property } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";
import type { UmbriciansFeatureFlagsConditionElement } from "../condition.extension.js";
import {
  browserOffsetAt,
  formatOffset,
  formatUtc,
  fromLocalInput,
  readEnd,
  toLocalInput,
  windowProblems,
  withEnd,
  type WindowEndName,
  type WindowProblem,
} from "./time-window.js";

/**
 * The editor of the time window condition (`Microsoft.TimeWindow`): "On from ... until ...", either end optional. Times are
 * entered and shown in the browser's time zone, with the UTC equivalent beside each, and are stored as UTC.
 * It takes the condition's parameters in `value` and, when an editor changes them, sets `value` to a new object and fires `change`.
 */
@customElement("umbricians-feature-flags-time-window-condition")
export class TimeWindowConditionElement extends UmbLitElement implements UmbriciansFeatureFlagsConditionElement {
  /** The condition's parameters, as Microsoft.TimeWindow reads them. */
  @property({ attribute: false })
  value: Record<string, unknown> = {};

  @property({ type: Boolean, reflect: true })
  readonly = false;

  #change(end: WindowEndName, text: string) {
    this.value = withEnd(this.value, end, fromLocalInput(text, browserOffsetAt));
    this.dispatchEvent(new UmbChangeEvent());
  }

  override render() {
    const problems = windowProblems(this.value);
    return html`<p class="summary">${this.localize.term("umbriciansFeatureFlags_onFromUntil")}</p>
      ${this.#renderEnd("Start", "umbriciansFeatureFlags_windowStart")}
      ${this.#renderEnd("End", "umbriciansFeatureFlags_windowEnd")}
      <small>${this.localize.term("umbriciansFeatureFlags_windowZone", formatOffset(browserOffsetAt(Date.now())))}</small>
      ${problems.map((problem) => html`<p class="problem">${this.#problemText(problem)}</p>`)}`;
  }

  #renderEnd(end: WindowEndName, labelTerm: "umbriciansFeatureFlags_windowStart" | "umbriciansFeatureFlags_windowEnd") {
    const read = readEnd(this.value, end);
    const label = this.localize.term(labelTerm);
    return html`<div class="end">
      <span class="label">${label}</span>
      <uui-input
        type="datetime-local"
        label=${label}
        .value=${read.kind === "at" ? toLocalInput(read.instant, browserOffsetAt) : ""}
        ?readonly=${this.readonly}
        @change=${(e: Event) => {
          e.stopPropagation();
          this.#change(end, (e.target as HTMLInputElement).value);
        }}></uui-input>
      ${read.kind === "at"
        ? html`<small>${this.localize.term("umbriciansFeatureFlags_windowUtc", formatUtc(read.instant))}</small>`
        : read.kind === "unreadable"
          ? html`<small class="problem">${this.localize.term("umbriciansFeatureFlags_windowUnreadable", read.text)}</small>`
          : html`<small>${this.localize.term("umbriciansFeatureFlags_windowNoLimit")}</small>`}
    </div>`;
  }

  #problemText(problem: WindowProblem) {
    switch (problem) {
      case "neither":
        return this.localize.term("umbriciansFeatureFlags_windowNeither");
      case "startNotBeforeEnd":
        return this.localize.term("umbriciansFeatureFlags_windowOrder");
      default:
        return nothing;
    }
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
    .end {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: var(--uui-size-space-2) var(--uui-size-space-4);
      margin-bottom: var(--uui-size-space-3);
    }
    .label {
      min-width: 4em;
    }
    .problem {
      color: var(--uui-color-danger-standalone);
    }
  `;
}

export { TimeWindowConditionElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-time-window-condition": TimeWindowConditionElement;
  }
}
