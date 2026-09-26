import type { ButtonHTMLAttributes, PropsWithChildren } from "react";
import "./Button.css";

type ButtonVariant = "primary" | "danger" | "secondary";

type Props = PropsWithChildren<ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
}>;

export function Button({ variant = "primary", className = "", children, ...props }: Props) {
  return <button className={`button button--${variant} ${className}`.trim()} {...props}>{children}</button>;
}
