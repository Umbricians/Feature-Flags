/** A failed Feature Flags call: the HTTP status (undefined when there was no response) and the server's own explanation. */
export class FeatureFlagsRequestError extends Error {
  status: number | undefined;
  detail: string | undefined;

  constructor(status: number | undefined, detail: string | undefined) {
    // The message is for logs; screens word the failure for the user from the status and detail.
    super(detail || `Feature Flags request failed (${status ?? "no response"})`);
    this.status = status;
    this.detail = detail;
  }
}
