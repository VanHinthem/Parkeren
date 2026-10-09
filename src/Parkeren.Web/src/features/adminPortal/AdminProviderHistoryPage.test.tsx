// @vitest-environment happy-dom
import { cleanup,fireEvent,render,screen,waitFor } from "@testing-library/react";
import { afterEach,describe,expect,it,vi } from "vitest";
import { assignAdminProviderActionUser,getAdminProviderActionHistory,getAdminProviderAssignmentHistory,getAdminProviderHistorySyncStatus,startAdminProviderHistorySync,getAdminProviderProducts,getUsers } from "../../api/client";
import { AdminProviderHistoryPage } from "./AdminProviderHistoryPage";

vi.mock("../../api/client",()=>({
  assignAdminProviderActionUser:vi.fn(),
  getAdminProviderActionHistory:vi.fn(),
  getAdminProviderAssignmentHistory:vi.fn(),
  getAdminProviderHistorySyncStatus:vi.fn(),
  startAdminProviderHistorySync:vi.fn(),
  getAdminProviderProducts:vi.fn(),
  getUsers:vi.fn()
}));

afterEach(()=>{cleanup();vi.resetAllMocks();});

const action=(origin:"Managed"|"Imported",assignedUserId:string|null)=>({
  id:"action-1",providerActionId:"external-1",visitId:origin==="Managed"?"visit-1":null,
  providerProductId:"visitor",licensePlate:"AA11BB",actualStartAt:"2026-10-08T08:00:00Z",
  actualEndAt:"2026-10-08T09:00:00Z",providerCostAmount:null,
  state:"Completed",origin,assignedUserId,assignmentSource:"Unassigned",
  historyStatus:"Reconciled",username:origin==="Managed"?"Ramon":null
});

function setup(origin:"Managed"|"Imported"){
  vi.mocked(getAdminProviderHistorySyncStatus).mockResolvedValue({states:[],runs:[]});
  vi.mocked(getAdminProviderProducts).mockResolvedValue([]);
  vi.mocked(getUsers).mockResolvedValue([
    {id:"user-1",username:"Ramon",role:"Visitor",status:"Active",isActive:true,canDelete:false,maxConcurrentVisits:null}
  ]);
  vi.mocked(getAdminProviderActionHistory).mockResolvedValue({
    items:[action(origin,null)],page:1,pageSize:25,totalCount:1
  });
  vi.mocked(getAdminProviderAssignmentHistory).mockResolvedValue([]);
}

describe("AdminProviderHistoryPage assignment",()=>{
  it("shows visit user without manual assignment for managed actions",async()=>{
    setup("Managed");
    render(<AdminProviderHistoryPage/>);
    fireEvent.click(await screen.findByRole("button",{name:"Details"}));
    expect(await screen.findByText("Via Visit")).toBeTruthy();
    expect(screen.getAllByText("Ramon").length).toBeGreaterThan(0);
    expect(screen.queryByLabelText("Gebruiker toewijzen")).toBeNull();
  });
  it("passes status, origin, user and discrepancy filters to the history API",async()=>{
    setup("Imported");
    render(<AdminProviderHistoryPage/>);
    await screen.findByRole("button",{name:"Details"});
    fireEvent.change(screen.getByLabelText("Status"),{target:{value:"Completed"}});
    fireEvent.change(screen.getByLabelText("Herkomst"),{target:{value:"Imported"}});
    fireEvent.change(screen.getByLabelText("Gebruiker"),{target:{value:"user-1"}});
    fireEvent.change(screen.getByLabelText("Afwijking"),{target:{value:"open"}});
    fireEvent.change(screen.getByLabelText("Sortering"),{target:{value:"asc"}});
    await waitFor(()=>expect(getAdminProviderActionHistory).toHaveBeenCalledWith(
      expect.objectContaining({
        state:"Completed",origin:"Imported",assignedUserId:"user-1",
        hasOpenDiscrepancy:true,oldestFirst:true,page:1
      })
    ));
  });
  it("assigns and removes an imported action user",async()=>{
    setup("Imported");
    vi.mocked(assignAdminProviderActionUser).mockResolvedValue({changed:true});
    render(<AdminProviderHistoryPage/>);
    fireEvent.click(await screen.findByRole("button",{name:"Details"}));
    const selection=await screen.findByLabelText("Gebruiker toewijzen");
    fireEvent.change(selection,{target:{value:"user-1"}});
    fireEvent.click(screen.getByRole("button",{name:"Toewijzing opslaan"}));
    await waitFor(()=>expect(assignAdminProviderActionUser).toHaveBeenCalledWith("action-1","user-1"));
    const reloadedSelection=await screen.findByLabelText("Gebruiker toewijzen");
    await screen.findByRole("button",{name:"Toewijzing opslaan"});
    fireEvent.change(reloadedSelection,{target:{value:""}});
    fireEvent.click(screen.getByRole("button",{name:"Toewijzing opslaan"}));
    await waitFor(()=>expect(assignAdminProviderActionUser).toHaveBeenCalledWith("action-1",null));
  });
});
