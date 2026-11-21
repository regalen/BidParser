import { Fragment, type ReactNode } from 'react';

const allowedSchemes = new Set(['http:', 'https:', 'mailto:']);

/**
 * Renders announcement HTML as React elements from a fixed allowlist (bold, italic, underline,
 * line breaks and http/https/mailto links) instead of injecting markup. The server already
 * sanitises stored announcements; this keeps the banner and the admin's live preview safe even
 * for unsaved input. DOMParser documents never execute scripts or load resources.
 */
export function renderAnnouncementHtml(html: string): ReactNode {
  const body = new DOMParser().parseFromString(html, 'text/html').body;
  return renderChildren(body);
}

function renderChildren(parent: Node): ReactNode[] {
  return Array.from(parent.childNodes).map((node, index) => renderNode(node, index));
}

function renderNode(node: Node, key: number): ReactNode {
  if (node.nodeType === Node.TEXT_NODE) return node.textContent;
  if (node.nodeType !== Node.ELEMENT_NODE) return null;

  const element = node as Element;
  const children = renderChildren(element);
  switch (element.tagName.toLowerCase()) {
    case 'strong':
    case 'b':
      return <strong key={key} className="font-semibold">{children}</strong>;
    case 'em':
    case 'i':
      return <em key={key}>{children}</em>;
    case 'u':
      return <u key={key}>{children}</u>;
    case 'br':
      return <br key={key} />;
    case 'a': {
      const href = safeHref(element.getAttribute('href'));
      if (!href) return <Fragment key={key}>{children}</Fragment>;
      return (
        <a
          key={key}
          href={href}
          target="_blank"
          rel="noopener noreferrer"
          className="font-medium text-accent underline decoration-solid underline-offset-2 hover:no-underline"
        >
          {children}
        </a>
      );
    }
    default:
      // Unsupported elements degrade to their text, matching what the server would refuse to save.
      return <Fragment key={key}>{children}</Fragment>;
  }
}

function safeHref(value: string | null): string | null {
  if (!value) return null;
  try {
    const url = new URL(value, window.location.origin);
    return allowedSchemes.has(url.protocol) ? url.href : null;
  } catch {
    return null;
  }
}
