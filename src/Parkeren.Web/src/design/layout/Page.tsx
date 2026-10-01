import type { PropsWithChildren,ReactNode } from "react";
import "./Page.css";
export function Page({title,subtitle,children}:PropsWithChildren<{title:string;subtitle?:ReactNode}>){return <div className="page"><header className="page__heading"><h1>{title}</h1>{subtitle&&<div>{subtitle}</div>}</header>{children}</div>}