import { UmbContextBase } from "@umbraco-cms/backoffice/class-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";
import { UmbArrayState, UmbBasicState, UmbBooleanState, UmbObjectState } from "@umbraco-cms/backoffice/observable-api";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { umbConfirmModal } from "@umbraco-cms/backoffice/modal";
import {
  UMB_WORKSPACE_CONTEXT,
  UmbWorkspaceRouteManager,
  type UmbRoutableWorkspaceContext,
} from "@umbraco-cms/backoffice/workspace";
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
import { FEATURE_FLAG_ENTITY_TYPE, FEATURE_FLAG_WORKSPACE_ALIAS } from "../../constants.js";
import { FEATURE_FLAGS_PERMISSION_MANAGE } from "../../permissions/constants.js";
import { observePermission } from "../../permissions/permissions.js";
import { stateLabel } from "../describe.js";
import { FeatureFlagEditorElement } from "./flag-editor.element.js";

/** What the editor's state choice means: off (keeping any audience), on for everyone, or on for an audience. */
export type FlagEditorMode = "off" | "everyone" | "audience";

const emptyAudience = (): FlagAudience => ({ members: [], memberGroups: [], groups: [] });

/**
 * The flag editor's workspace, at …/edit/{name}: loads one flag, holds the editor's unsaved changes, and saves or resets
 * them. Its tabs (Settings, Definition) are workspace views that read and change this shared state.
 */
export class FeatureFlagWorkspaceContext extends UmbContextBase implements UmbRoutableWorkspaceContext {
  readonly workspaceAlias = FEATURE_FLAG_WORKSPACE_ALIAS;
  readonly routes = new UmbWorkspaceRouteManager(this);

  #flag = new UmbObjectState<FeatureFlag | undefined>(undefined);
  readonly flag = this.#flag.asObservable();

  #providerGroups = new UmbArrayState<ProviderGroup>([], (group) => group.id);
  readonly providerGroups = this.#providerGroups.asObservable();

  #mode = new UmbBasicState<FlagEditorMode>("off");
  readonly mode = this.#mode.asObservable();

  #audience = new UmbObjectState<FlagAudience>(emptyAudience());
  readonly audience = this.#audience.asObservable();

  #dirty = new UmbBooleanState(false);
  readonly dirty = this.#dirty.asObservable();

  #saving = new UmbBooleanState(false);
  readonly saving = this.#saving.asObservable();

  #loading = new UmbBooleanState(true);
  readonly loading = this.#loading.asObservable();

  #notFound = new UmbBooleanState(false);
  readonly notFound = this.#notFound.asObservable();

  #canManage = new UmbBooleanState(false);
  /** Whether the current user has the Change feature flags permission. */
  readonly canManage = this.#canManage.asObservable();

  #name?: string;
  #localize = new UmbLocalizationController(this);
  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor(host: UmbControllerHost) {
    super(host, UMB_WORKSPACE_CONTEXT);
    observePermission(this, FEATURE_FLAGS_PERMISSION_MANAGE, (permitted) => this.#canManage.setValue(permitted));
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
    this.routes.setRoutes([
      {
        path: "edit/:name",
        component: FeatureFlagEditorElement,
        setup: (_component, info) => this.load(decodeName(info.match.params.name)),
      },
    ]);
  }

  getEntityType() {
    return FEATURE_FLAG_ENTITY_TYPE;
  }

  /** Whether the flag can be changed here: the backoffice can edit it and the user may change flags. */
  isEditable() {
    return !!this.#flag.getValue()?.flag.editable && this.#canManage.getValue();
  }

  async load(name = this.#name) {
    if (!name) return;
    this.#name = name;
    this.#loading.setValue(true);
    try {
      const [flag, providerGroups] = await Promise.all([
        fetchFlag(name),
        fetchProviderGroups().catch(() => [] as Array<ProviderGroup>),
      ]);
      this.#flag.setValue(flag);
      this.#providerGroups.setValue(providerGroups);
      this.#audience.setValue(flag.audienceDetail ?? emptyAudience());
      this.#mode.setValue(!flag.flag.enabled ? "off" : flag.audienceDetail ? "audience" : "everyone");
      this.#dirty.setValue(false);
      this.#notFound.setValue(false);
    } catch (error) {
      const notFound = error instanceof FeatureFlagsRequestError && error.status === 404;
      this.#notFound.setValue(notFound);
      if (!notFound) this.#fail(error);
    } finally {
      this.#loading.setValue(false);
    }
  }

  setMode(mode: FlagEditorMode) {
    this.#mode.setValue(mode);
    this.#dirty.setValue(true);
  }

  updateAudience(change: Partial<FlagAudience>) {
    this.#audience.update(change);
    this.#dirty.setValue(true);
  }

  toggleProviderGroup(id: string, checked: boolean) {
    const groups = this.#audience.getValue().groups;
    this.updateAudience({ groups: checked ? [...new Set([...groups, id])] : groups.filter((group) => group !== id) });
  }

  async save() {
    const flag = this.#flag.getValue();
    if (!flag || !this.isEditable()) return;
    const mode = this.#mode.getValue();
    this.#saving.setValue(true);
    try {
      await saveFlag(flag.flag.name, mode !== "off", this.#audienceToSave(flag));
      this.#notifications?.peek("positive", {
        data: { message: this.#localize.term("umbriciansFeatureFlags_saved", flag.flag.name) },
      });
      await this.load();
    } catch (error) {
      this.#fail(error);
    } finally {
      this.#saving.setValue(false);
    }
  }

  async reset() {
    const flag = this.#flag.getValue();
    if (!flag) return;
    try {
      await umbConfirmModal(this, {
        headline: this.#localize.term("umbriciansFeatureFlags_resetHeadline", flag.flag.name),
        content: this.#localize.term("umbriciansFeatureFlags_resetMessage", stateLabel(this.#localize, flag.defaultState)),
        color: "warning",
        confirmLabel: this.#localize.term("umbriciansFeatureFlags_reset"),
      });
    } catch {
      return;
    }
    this.#saving.setValue(true);
    try {
      await resetFlag(flag.flag.name);
      this.#notifications?.peek("positive", {
        data: { message: this.#localize.term("umbriciansFeatureFlags_resetDone", flag.flag.name) },
      });
      await this.load();
    } catch (error) {
      this.#fail(error);
    } finally {
      this.#saving.setValue(false);
    }
  }

  /** The audience to save: the chosen one when on for an audience, the one kept while off, or none for everyone. */
  #audienceToSave(flag: FeatureFlag): FlagAudience | null {
    const mode = this.#mode.getValue();
    const audience = this.#audience.getValue();
    if (mode === "everyone") return null;
    if (mode === "audience") return audience;
    const kept = audience.members.length + audience.memberGroups.length + audience.groups.length > 0;
    return kept || flag.audienceDetail ? audience : null;
  }

  #fail(error: unknown) {
    this.#notifications?.peek("danger", {
      data: {
        headline: this.#localize.term("umbriciansFeatureFlags_changeFailed"),
        message: error instanceof FeatureFlagsRequestError && error.detail ? error.detail : String(error),
      },
    });
  }
}

function decodeName(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    // Already decoded, and holds a % of its own.
    return value;
  }
}

export { FeatureFlagWorkspaceContext as api };
