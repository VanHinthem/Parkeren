import { afterEach,expect,it,vi } from "vitest";
import { getActiveVisit,startVisit } from "./client";

const originalOnlineDescriptor=Object.getOwnPropertyDescriptor(navigator,"onLine");

afterEach(()=>{
  if(originalOnlineDescriptor)
    Object.defineProperty(navigator,"onLine",originalOnlineDescriptor);
  else
    Reflect.deleteProperty(navigator,"onLine");
  vi.restoreAllMocks();
});

it("does not send a parking mutation when the browser reports offline",async()=>{
  Object.defineProperty(navigator,"onLine",{configurable:true,value:false});
  const fetch=vi.spyOn(globalThis,"fetch");

  await expect(startVisit("vehicle-id",null,"operation-id")).rejects.toThrow("Je bent offline.");
  expect(fetch).not.toHaveBeenCalled();
});

it("asks users to verify parking status when a Start response body cannot be read",async()=>{
  Object.defineProperty(navigator,"onLine",{configurable:true,value:true});
  vi.spyOn(globalThis,"fetch")
    .mockResolvedValueOnce(new Response(JSON.stringify({token:"csrf-token"}),{status:200}))
    .mockResolvedValueOnce({
      ok:true,
      status:200,
      json:vi.fn().mockRejectedValue(new TypeError("The operation was aborted."))
    } as unknown as Response);

  await expect(startVisit("vehicle-id",null,"operation-id"))
    .rejects.toThrow("Controleer de actuele parkeerstatus voordat je een actie opnieuw probeert.");
});

it("asks users to verify parking status when a Start error body cannot be read",async()=>{
  Object.defineProperty(navigator,"onLine",{configurable:true,value:true});
  vi.spyOn(globalThis,"fetch").mockImplementation(async input=>{
    if(String(input)==="/api/auth/csrf")
      return new Response(JSON.stringify({token:"csrf-token"}),{status:200});
    return {
      ok:false,
      status:503,
      json:vi.fn().mockRejectedValue(new TypeError("The operation was aborted."))
    } as unknown as Response;
  });

  await expect(startVisit("vehicle-id",null,"operation-id"))
    .rejects.toThrow("Controleer de actuele parkeerstatus voordat je een actie opnieuw probeert.");
});

it("does not use browser cache for active Visit reads",async()=>{
  Object.defineProperty(navigator,"onLine",{configurable:true,value:true});
  const fetch=vi.spyOn(globalThis,"fetch").mockResolvedValue(new Response(null,{status:404}));

  await getActiveVisit();

  expect(fetch).toHaveBeenCalledWith("/api/visits/active",expect.objectContaining({cache:"no-store"}));
});