// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StartVisitCard } from "./StartVisitCard";

vi.mock("../api/client", () => ({ previewVisitStart: vi.fn() }));
afterEach(() => { cleanup(); vi.clearAllMocks(); });

const vehicles = [
  { id: "vehicle-1", licensePlate: "PZ493F", isActive: true },
  { id: "vehicle-2", licensePlate: "KHB55N", isActive: true }
] as Parameters<typeof StartVisitCard>[0]["vehicles"];

describe("StartVisitCard license plate picker", () => {
  it("shows the plate without a dropdown when there is one vehicle", () => {
    render(<StartVisitCard vehicles={[vehicles[0]]} onStart={vi.fn()} />);
    expect(screen.getByLabelText("Kenteken PZ493F")).toBeTruthy();
    expect(screen.queryByRole("combobox", { name: "Auto kiezen" })).toBeNull();
  });

  it("shows a styled selected plate and native option list for multiple vehicles", () => {
    render(<StartVisitCard vehicles={vehicles} onStart={vi.fn()} />);
    const select = screen.getByRole("combobox", { name: "Auto kiezen" });
    expect(screen.getByLabelText("Kenteken PZ493F")).toBeTruthy();
    expect(screen.getAllByRole("option").map(option => option.textContent))
      .toEqual(["PZ493F", "KHB55N"]);
    fireEvent.change(select, { target: { value: "vehicle-2" } });
    expect(screen.getByLabelText("Kenteken KHB55N")).toBeTruthy();
    expect((select as HTMLSelectElement).value).toBe("vehicle-2");
  });
});
