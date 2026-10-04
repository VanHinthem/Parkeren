// @vitest-environment happy-dom
import { cleanup,fireEvent,render,screen } from "@testing-library/react";
import { afterEach,expect,it } from "vitest";
import { NetworkStatusBanner } from "./NetworkStatusBanner";

const originalOnlineDescriptor=Object.getOwnPropertyDescriptor(navigator,"onLine");

afterEach(()=>{
  cleanup();
  if(originalOnlineDescriptor)
    Object.defineProperty(navigator,"onLine",originalOnlineDescriptor);
  else
    Reflect.deleteProperty(navigator,"onLine");
});

it("shows stale-data warning while offline and clears it after reconnecting",()=>{
  Object.defineProperty(navigator,"onLine",{configurable:true,value:false});
  render(<NetworkStatusBanner/>);

  expect(screen.getByRole("status").textContent).toContain("Getoonde parkeerinformatie kan verouderd zijn");

  Object.defineProperty(navigator,"onLine",{configurable:true,value:true});
  fireEvent(window,new Event("online"));
  expect(screen.queryByRole("status")).toBeNull();
});