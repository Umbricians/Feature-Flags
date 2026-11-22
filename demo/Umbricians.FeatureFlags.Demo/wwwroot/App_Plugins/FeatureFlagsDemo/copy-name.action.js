// An entity action for flags: `unique` is the flag's name. Copies it, and says so either way.
import { UmbEntityActionBase } from "@umbraco-cms/backoffice/entity-action";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";

export class DemoCopyFlagNameAction extends UmbEntityActionBase {
  async execute() {
    const name = this.args.unique;
    let copied = false;
    try {
      await navigator.clipboard.writeText(name);
      copied = true;
    } catch {
      // The clipboard can refuse, such as when the page isn't focused; the notification still names the flag.
    }

    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    notifications?.peek("positive", {
      data: { headline: copied ? "Copied" : "Flag name", message: name },
    });
  }
}

export { DemoCopyFlagNameAction as api };
