/** A failed Feature Flags call: the HTTP status (undefined when there was no response) and the server's own explanation. */
export class FeatureFlagsRequestError extends Error {
  status: number | undefined;
  detail: string | undefined;
  /** The problems the server listed with the refusal, such as what is wrong with each condition. */
  problems: Array<string>;

  constructor(status: number | undefined, detail: string | undefined, problems: Array<string> = []) {
    // The message is for logs; screens word the failure for the user from the status, detail and problems.
    super(detail || problems.join(" ") || `Feature Flags request failed (${status ?? "no response"})`);
    this.status = status;
    this.detail = detail;
    this.problems = problems;
  }
}

const text = (value: unknown) => (typeof value === "string" && value.trim() ? value.trim() : undefined);

/** Reads the server's problem details (`detail`, falling back to `title`, and the `problems` or `messages` list) from an error body. */
export function readProblem(body: unknown): { detail: string | undefined; problems: Array<string> } {
  if (typeof body === "string") {
    try {
      return readProblem(JSON.parse(body));
    } catch {
      return { detail: text(body), problems: [] };
    }
  }
  if (typeof body !== "object" || body === null) return { detail: undefined, problems: [] };
  const record = body as Record<string, unknown>;
  const list = (value: unknown) => (Array.isArray(value) ? value.map(text).filter((item): item is string => item !== undefined) : []);
  const problems = list(record.problems);
  const detail = text(record.detail) ?? (problems.length ? undefined : text(record.title));
  return { detail, problems: problems.filter((problem) => problem !== detail) };
}

/**
 * What to tell the user about a failed call: the server's reason, then each problem it listed.
 * Anything that is not a request error is worded as it is.
 */
export function describeFailure(error: unknown): string {
  if (!(error instanceof FeatureFlagsRequestError)) return error instanceof Error ? error.message : String(error);
  const lines = [error.detail, ...error.problems].filter((line): line is string => !!line);
  return lines.length ? lines.join(" ") : error.message;
}
