import type { InputHTMLAttributes } from "react";
import "./Input.css";

type Props = InputHTMLAttributes<HTMLInputElement> & { label: string; error?: string };

export function Input({ label, error, id, ...props }: Props) {
  const inputId = id ?? `input-${props.name ?? "field"}`;
  const errorId = `${inputId}-error`;
  return <label className="field" htmlFor={inputId}><span className="field__label">{label}</span><input id={inputId} aria-invalid={Boolean(error)} aria-describedby={error ? errorId : undefined} {...props}/>{error && <span id={errorId} className="field__error">{error}</span>}</label>;
}
