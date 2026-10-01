import type { PropsWithChildren } from "react";
import "./Alert.css";
type Tone="info"|"warning"|"danger";
export function Alert({tone="info",children}:PropsWithChildren<{tone?:Tone}>){return <div className={`alert alert--${tone}`} role={tone==="danger"?"alert":"status"}>{children}</div>}