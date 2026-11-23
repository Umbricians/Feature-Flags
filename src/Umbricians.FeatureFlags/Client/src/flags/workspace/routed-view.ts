/** The part of a workspace view manifest that routing needs. */
export type RoutableView = { alias: string; meta: { pathname: string } };

/**
 * Works out which of the editor's workspace views the URL is showing, so its title can be made the browser tab's.
 * `views` is in tab order; `path` is the URL's path (a query string or hash is ignored).
 * The URL is …/edit/{name}[/view/{pathname}]: no view segment means the first tab. Returns null when the URL names a view
 * that doesn't exist, or when there are no views.
 */
export function routedViewAlias(views: ReadonlyArray<RoutableView>, path: string): string | null {
  if (views.length === 0) return null;
  const clean = path.split(/[?#]/)[0].replace(/\/+$/, "");
  // The view segment is always the last two. A flag name that holds "/view/" is encoded in the URL, so it never matches here.
  const match = /\/view\/([^/]+)$/.exec(clean);
  if (!match) return views[0].alias;
  const pathname = decode(match[1]);
  return views.find((view) => view.meta.pathname === pathname)?.alias ?? null;
}

function decode(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}
