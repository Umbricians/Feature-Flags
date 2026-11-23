import { UmbEntityContext } from "@umbraco-cms/backoffice/entity";
import { UmbContextBase } from "@umbraco-cms/backoffice/class-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";
import { UmbArrayState, UmbBooleanState, UmbObjectState } from "@umbraco-cms/backoffice/observable-api";
import { umbExtensionsRegistry } from "@umbraco-cms/backoffice/extension-registry";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { UMB_DISCARD_CHANGES_MODAL, umbConfirmModal, umbOpenModal } from "@umbraco-cms/backoffice/modal";
import { UmbViewContext } from "@umbraco-cms/backoffice/view";
import {
  UMB_WORKSPACE_CONTEXT,
  UmbWorkspaceRouteManager,
  umbWorkspaceWillNavigateAway,
  type UmbRoutableWorkspaceContext,
  type UmbWorkspaceEditorContext,
} from "@umbraco-cms/backoffice/workspace";
import {
  fetchConditions,
  fetchFlag,
  fetchProviderGroups,
  resetFlag,
  saveFlag,
  type ConditionMatch,
  type FeatureFlag,
  type FlagCondition,
  type ProviderGroup,
  type RegisteredCondition,
} from "../../api/feature-flags.resource.js";
import { FeatureFlagsRequestError, describeFailure } from "../../api/request-error.js";
import { FEATURE_FLAG_ENTITY_TYPE, FEATURE_FLAG_WORKSPACE_ALIAS } from "../../constants.js";
import { flagEntity } from "../entity.js";
import { FeatureFlagPublicWorkspaceContext } from "./flag-workspace.public-context.js";
import { FEATURE_FLAGS_PERMISSION_MANAGE } from "../../permissions/constants.js";
import { observePermission } from "../../permissions/permissions.js";
import { addableConditions } from "../../conditions/condition-names.js";
import { stateLabel } from "../describe.js";
import { mayDo } from "../display.js";
import { routedViewAlias } from "./routed-view.js";
import { FeatureFlagEditorElement } from "./flag-editor.element.js";
import {
  addCondition,
  canSave,
  loadEditor,
  removeCondition,
  saveBlocker,
  savePayload,
  setConditionParameters,
  setMatch,
  setMode,
  type FlagEditorMode,
  type FlagEditorState,
  type SaveBlocker,
} from "./flag-editor-state.js";

/** One of the workspace editor's tabs: its manifest, and its view context's internal activation. */
type EditorTab = { manifest: { alias: string; meta: { pathname: string } }; _internal_activate?: () => void };

/**
 * The flag editor's workspace, at …/edit/{name}: loads one flag, holds the editor's unsaved changes, and saves or resets
 * them. Its tabs (Settings, Definition) are workspace views that read and change this shared state.
 */
export class FeatureFlagWorkspaceContext extends UmbContextBase implements UmbRoutableWorkspaceContext {
  readonly workspaceAlias = FEATURE_FLAG_WORKSPACE_ALIAS;
  readonly routes = new UmbWorkspaceRouteManager(this);

  // Gives Umbraco's entity actions (the workspace's actions menu) the flag's entity type and name.
  #entity = new UmbEntityContext(this);
  /** The flag's name, once the route has it. Umbraco's workspace action menu reads this. */
  readonly unique = this.#entity.unique;

  #flag = new UmbObjectState<FeatureFlag | undefined>(undefined);
  readonly flag = this.#flag.asObservable();

  #providerGroups = new UmbArrayState<ProviderGroup>([], (group) => group.id);
  readonly providerGroups = this.#providerGroups.asObservable();

  #editor = new UmbObjectState<FlagEditorState | undefined>(undefined);
  readonly mode = this.#editor.asObservablePart((state) => state?.mode ?? "off");
  readonly conditions = this.#editor.asObservablePart((state) => state?.conditions ?? ([] as ReadonlyArray<FlagCondition>));
  readonly match = this.#editor.asObservablePart((state): ConditionMatch => state?.match ?? "any");
  /** Whether adding the Root condition just switched the match to All. */
  readonly matchSwitchedForRoot = this.#editor.asObservablePart((state) => state?.matchSwitchedForRoot ?? false);
  readonly dirty = this.#editor.asObservablePart((state) => state?.dirty ?? false);

  #registered = new UmbArrayState<RegisteredCondition>([], (condition) => condition.name);
  /** The conditions registered in C#, which flags can use. */
  readonly registered = this.#registered.asObservable();

  #editorNames = new UmbArrayState<string>([], (name) => name);
  /** The `meta.name` of every condition editor the backoffice has. */
  readonly editorNames = this.#editorNames.asObservable();

  #addable = new UmbArrayState<RegisteredCondition>([], (condition) => condition.name);
  /** The conditions the Add menu offers: registered, with an editor, and not yet on the flag. */
  readonly addable = this.#addable.asObservable();

  #blocker = new UmbObjectState<{ reason: SaveBlocker | null }>({ reason: null });
  /** Why the editor can't save what it holds, beyond what the catalog says; null when nothing blocks it. */
  readonly saveBlocker = this.#blocker.asObservablePart((blocker) => blocker.reason);

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
  #allowNavigateAway = false;
  /** Gives the browser tab the flag's name, rather than the name of the Settings tab twice. */
  #view = new UmbViewContext(this, null);
  /** The workspace editor's tabs (one view context each, in tab order), once the editor exists. */
  #tabs: ReadonlyArray<EditorTab> = [];
  #localize = new UmbLocalizationController(this);
  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor(host: UmbControllerHost) {
    super(host, UMB_WORKSPACE_CONTEXT);
    this.#entity.setEntityType(FEATURE_FLAG_ENTITY_TYPE);
    // The read-only facade developers use; the editing context above stays internal.
    new FeatureFlagPublicWorkspaceContext(this, this.flag);
    observePermission(this, FEATURE_FLAGS_PERMISSION_MANAGE, (permitted) => this.#canManage.setValue(permitted));
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
    this.observe(
      umbExtensionsRegistry.byType("umbriciansFeatureFlagsCondition"),
      (manifests) => {
        this.#editorNames.setValue(manifests.map((manifest) => manifest.meta.name));
        this.#refresh();
      },
      "umbriciansConditionEditors",
    );
    // Leaving the editor with unsaved changes asks first, as Umbraco's own workspaces do.
    window.addEventListener("willchangestate", this.#onWillNavigate);
    window.addEventListener("beforeunload", this.#onBeforeUnload);
    this.routes.setRoutes([
      {
        path: "edit/:name",
        component: FeatureFlagEditorElement,
        setup: (_component, info) => this.load(decodeName(info.match.params.name)),
      },
    ]);
  }

  #onWillNavigate = async (event: Event) => {
    if (this.#allowNavigateAway || !this.#editor.getValue()?.dirty) return true;
    const url = (event as CustomEvent<{ url: string | URL }>).detail.url;
    if (!umbWorkspaceWillNavigateAway(this.routes, this.getUnique(), url)) return true;
    // The modal is asynchronous and the event is not, so stop the navigation now and push it again once discarding is confirmed.
    event.preventDefault();
    try {
      await umbOpenModal(this, UMB_DISCARD_CHANGES_MODAL);
      this.#allowNavigateAway = true;
      history.pushState({}, "", url);
      return true;
    } catch {
      // Stayed on the flag; the closed modal has left the section's title behind.
      this.#reassertTitle();
      return false;
    }
  };

  #onBeforeUnload = (event: BeforeUnloadEvent) => {
    if (this.#editor.getValue()?.dirty) {
      event.preventDefault();
      event.returnValue = "";
    }
  };

  override destroy() {
    window.removeEventListener("willchangestate", this.#onWillNavigate);
    window.removeEventListener("beforeunload", this.#onBeforeUnload);
    super.destroy();
  }

  getEntityType() {
    return FEATURE_FLAG_ENTITY_TYPE;
  }

  getUnique() {
    return this.#entity.getUnique();
  }

  async load(name = this.#name) {
    if (!name) return;
    this.#name = name;
    this.#entity.setUnique(flagEntity(name).unique);
    this.#view.setTitle(name);
    this.#loading.setValue(true);
    try {
      const [flag, providerGroups, registered] = await Promise.all([
        fetchFlag(name),
        fetchProviderGroups().catch(() => [] as Array<ProviderGroup>),
        fetchConditions().catch(() => [] as Array<RegisteredCondition>),
      ]);
      this.#flag.setValue(flag);
      this.#providerGroups.setValue(providerGroups);
      this.#registered.setValue(registered);
      this.#editor.setValue(loadEditor(flag));
      this.#refresh();
      this.#notFound.setValue(false);
    } catch (error) {
      const notFound = error instanceof FeatureFlagsRequestError && error.status === 404;
      this.#notFound.setValue(notFound);
      if (!notFound) this.#fail(error);
    } finally {
      this.#loading.setValue(false);
      this.#reassertTitleAfterRender();
    }
  }

  /** Hands over the workspace editor (the frame that holds the tabs), whose tabs are what set the browser tab's title. */
  setEditorContext(context: UmbWorkspaceEditorContext | undefined) {
    if (!context) {
      this.#tabs = [];
      return;
    }
    this.observe(
      context.views,
      (views) => {
        this.#tabs = views as ReadonlyArray<EditorTab>;
        this.#reassertTitleAfterRender();
      },
      "umbriciansEditorViews",
    );
  }

  /**
   * Makes the browser tab's title "{flag name} | {tab} | Umbraco" again, for the tab the URL shows.
   *
   * Umbraco appears to get this wrong (an upstream bug). A modal hosted on the workspace deactivates the open view, and
   * on closing activates the *parent* view, so the title falls back to the section's ("Settings | Umbraco") and nothing
   * re-activates the routed tab. Also, every tab activates as it registers, so the last one registered (not the one
   * shown) can set the title. Activating the routed tab's view rewrites the title; no public method does that, so this
   * calls the internal `_internal_activate`, only if it exists.
   */
  #reassertTitle() {
    const alias = routedViewAlias(this.#tabs.map((tab) => tab.manifest), window.location.pathname);
    const tab = this.#tabs.find((candidate) => candidate.manifest.alias === alias);
    if (typeof tab?._internal_activate === "function") tab._internal_activate();
  }

  /**
   * Reasserts the title now and again once the tabs have rendered: on a full page load the tabs' views register (and
   * each activates, setting the title) after the views list arrives and after the flag loads, so the last word has to
   * come after their render.
   */
  #reassertTitleAfterRender() {
    this.#reassertTitle();
    requestAnimationFrame(() => requestAnimationFrame(() => this.#reassertTitle()));
  }

  setMode(mode: FlagEditorMode) {
    this.#apply((state) => setMode(state, mode));
  }

  /** Adds a condition by the name it is registered under. */
  addCondition(name: string) {
    this.#apply((state) => addCondition(state, name));
  }

  removeCondition(index: number) {
    this.#apply((state) => removeCondition(state, index));
  }

  setConditionParameters(index: number, parameters: Record<string, unknown>) {
    this.#apply((state) => setConditionParameters(state, index, parameters));
  }

  setMatch(match: ConditionMatch) {
    this.#apply((state) => setMatch(state, match));
  }

  async save() {
    const editor = this.#editor.getValue();
    if (!editor || !canSave(editor, this.#canManage.getValue(), this.#editorNames.getValue())) return;
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
    } finally {
      this.#reassertTitle();
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
    this.#refresh();
  }

  /** Works out what the editor offers and what blocks saving, from the editor's state and the editors that exist. */
  #refresh() {
    const state = this.#editor.getValue();
    this.#addable.setValue(state ? addableConditions(this.#registered.getValue(), this.#editorNames.getValue(), state.conditions) : []);
    this.#blocker.setValue({ reason: state ? saveBlocker(state, this.#editorNames.getValue()) : null });
  }

  #fail(error: unknown) {
    this.#notifications?.peek("danger", {
      data: {
        headline: this.#localize.term("umbriciansFeatureFlags_changeFailed"),
        message: describeFailure(error),
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
