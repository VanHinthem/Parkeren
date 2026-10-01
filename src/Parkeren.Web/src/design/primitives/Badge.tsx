import type { PropsWithChildren } from "react";
import "./Badge.css";

export function Badge({ children }: PropsWithChildren) {
  return <span className="badge">{children}</span>;
}
