import type { PropsWithChildren } from "react";
import { Button } from "./Button";
import "./Dialog.css";
type Props=PropsWithChildren<{open:boolean;title:string;onClose:()=>void}>;
export function Dialog({open,title,onClose,children}:Props){if(!open)return null;return <div className="dialog-backdrop" role="presentation" onMouseDown={e=>{if(e.currentTarget===e.target)onClose()}}><section className="dialog" role="dialog" aria-modal="true" aria-labelledby="dialog-title"><header><h2 id="dialog-title">{title}</h2><Button variant="secondary" aria-label="Sluiten" onClick={onClose}>×</Button></header><div>{children}</div></section></div>}