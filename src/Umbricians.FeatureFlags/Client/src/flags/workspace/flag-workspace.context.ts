import { UmbContextBase } from "@umbraco-cms/backoffice/class-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";
import { UmbArrayState, UmbBooleanState, UmbObjectState } from "@umbraco-cms/backoffice/observable-api";
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
  type ProviderGroup,
} from "../../api/feature-flags.resource.js";
import { FeatureFlagsRequestError } from "../../api/request-error.js";
import { FEATURE_FLAG_ENTITY_TYPE, FEATURE_FLAG_WORKSPACE_ALIAS } from "../../constants.js";
import { FEATURE_FLAGS_PERMISSION_MANAGE } from "../../permissions/constants.js";
import { observePermission } from "../../permissions/permissions.js";
import { emptyAudience } from "../audience.js";
import { stateLabel } from "../describe.js";
import { mayDo } from "../display.js";
import { FeatureFlagEditorElement } from "./flag-editor.element.js";
import {
  canSave,
  loadEditor,
  removeGroup,
  savePayload,
  setMemberGroupKeys,
  setMembers,
  setMode,
  setProviderGroup,
  type FlagEditorMode,
  type FlagEditorState,
} from "./flag-editor-state.js";

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

  #editor = new UmbObjectState<FlagEditorState | undefined>(undefined);
  readonly mode = this.#editor.asObservablePart((state) => state?.mode ?? "off");
  readonly audience = this.#editor.asObservablePart((state) => state?.audience ?? emptyAudience());
  readonly dirty = this.#editor.asObservablePart((state) => state?.dirty ?? false);

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
      this.#editor.setValue(loadEditor(flag));
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
    this.#apply((state) => setMode(state, mode));
  }

  setMembers(keys: Array<string>) {
    this.#apply((state) => setMembers(state, keys));
  }

  setMemberGroupKeys(keys: Array<string>) {
    this.#apply((state) => setMemberGroupKeys(state, keys));
  }

  setProviderGroup(group: ProviderGroup, checked: boolean) {
    this.#apply((state) => setProviderGroup(state, group, checked));
  }

  removeGroup(id: string) {
    this.#apply((state) => removeGroup(state, id));
  }

  async save() {
    const editor = this.#editor.getValue();
    if (!editor || !canSave(editor, this.#canManage.getValue())) return;
    const payload = savePayload(editor);
    this.#saving.setValue(true);
    try {
      await saveFlag(editor.flag.name, payload);
      this.#notifications?.peek("positive", {
        data: { message: this.#localize.term("umbriciansFeatureFlags_saved", editor.flag.name) },
      });
      await this.load();
    } catch (error) {
      this.#fail(error);
    } finally {
      this.#saving.setValue(false);
    }
  }

  /** Resets a declared flag to its default, or removes the setting of an undeclared one. */
  async reset() {
    const flag = this.#flag.getValue();
    if (!flag) return;
    const removing = !flag.isDeclared;
    if (!mayDo(flag, removing ? "remove" : "reset", this.#canManage.getValue()).allowed) return;
    try {
      await umbConfirmModal(
        this,
        removing
          ? {
              headline: this.#localize.term("umbriciansFeatureFlags_removeHeadline", flag.name),
              content: this.#localize.term("umbriciansFeatureFlags_removeMessage"),
              color: "danger",
              confirmLabel: this.#localize.term("umbriciansFeatureFlags_remove"),
            }
          : {
              headline: this.#localize.term("umbriciansFeatureFlags_resetHeadline", flag.name),
              content: this.#localize.term("umbriciansFeatureFlags_resetMessage", stateLabel(this.#localize, flag.defaultState)),
              color: "warning",
              confirmLabel: this.#localize.term("umbriciansFeatureFlags_reset"),
            },
      );
    } catch {
      return;
    }
    this.#saving.setValue(true);
    try {
      await resetFlag(flag.name);
      this.#notifications?.peek("positive", {
        data: {
          message: this.#localize.term(removing ? "umbriciansFeatureFlags_removed" : "umbriciansFeatureFlags_resetDone", flag.name),
        },
      });
      await this.load();
    } catch (error) {
      this.#fail(error);
    } finally {
      this.#saving.setValue(false);
    }
  }

  #apply(change: (state: FlagEditorState) => FlagEditorState) {
    const state = this.#editor.getValue();
    if (state) this.#editor.setValue(change(state));
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
