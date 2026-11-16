import { HttpErrorResponse } from '@angular/common/http';

export interface ApiProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
}

export function getApiErrorMessage(error: unknown, fallback: string): string {
  if (!(error instanceof HttpErrorResponse)) {
    return fallback;
  }

  if (error.status === 0) {
    return 'Unable to reach the server. Check your connection and try again.';
  }

  const message =
    getApiValidationMessage(error.error) ?? getApiProblemDetail(error.error);

  if (message) {
    return message;
  }

  if (error.status === 403) {
    return 'You do not have permission to perform this action.';
  }

  if (error.status >= 500) {
    return 'The server could not complete the request. Please try again.';
  }

  return fallback;
}

export async function getApiErrorMessageAsync(
  error: unknown,
  fallback: string
): Promise<string> {
  if (!(error instanceof HttpErrorResponse)) {
    return fallback;
  }

  if (error.error instanceof Blob) {
    try {
      const text = await error.error.text();
      const parsed: unknown = JSON.parse(text);
      const message =
        getApiValidationMessage(parsed) ?? getApiProblemDetail(parsed);

      if (message) {
        return message;
      }
    } catch {
      // Fall through to the status-based message below.
    }
  }

  return getApiErrorMessage(error, fallback);
}

export function getApiProblemDetail(value: unknown): string | null {
  if (!isRecord(value)) {
    return null;
  }

  const detail = value['detail'];

  if (typeof detail === 'string' && detail.trim().length > 0) {
    return detail.trim();
  }

  const title = value['title'];

  if (typeof title === 'string' && title.trim().length > 0) {
    return title.trim();
  }

  return null;
}

export function getApiValidationMessage(value: unknown): string | null {
  if (!isRecord(value)) {
    return null;
  }

  const errors = value['errors'];

  if (!isRecord(errors)) {
    return null;
  }

  for (const messages of Object.values(errors)) {
    if (!Array.isArray(messages)) {
      continue;
    }

    const message = messages.find(
      item => typeof item === 'string' && item.trim().length > 0
    );

    if (typeof message === 'string') {
      return message.trim();
    }
  }

  return null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
