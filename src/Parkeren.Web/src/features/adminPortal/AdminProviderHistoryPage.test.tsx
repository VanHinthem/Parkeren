// @vitest-environment happy-dom
import { cleanup,fireEvent,render,screen,waitFor } from "@testing-library/react";
import { afterEach,describe,expect,it,vi } from "vitest";
import { assignAdminProviderActionUser,getAdminProviderActionHistory,getAdminProviderAssignmentHistory,getAdminProviderHistorySyncStatus,startAdminProviderHistorySync,cancelAdminProviderHistorySync,getAdminProviderProducts,getUsers } from "../../api/client";
import { AdminProviderHistoryPage } from "./AdminProviderHistoryPage";

vi.mock("../../api/client",()=>({
  assignAdminProviderActionUser:vi.fn(),
  getAdminProviderActionHistory:vi.fn(),
  getAdminProviderAssignmentHistory:vi.fn(),
  getAdminProviderHistorySyncStatus:vi.fn(),
  startAdminProviderHistorySync:vi.fn(),
  cancelAdminProviderHistorySync:vi.fn(),
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

describe("AdminProviderHistoryPage synchronization",()=>{
  const runningRun={
    id:"run-1",providerProductId:"visitor",mode:"Manual" as const,status:"Running" as const,
    startedAt:"2026-10-09T09:00:00Z",finishedAt:null,
    readCount:10,insertedCount:6,refreshedCount:3,skippedCount:1,error:null
  };
  function setupSync(){
    setup("Imported");
    vi.mocked(getAdminProviderProducts).mockResolvedValue([{
      id:"product-1",providerProductId:"visitor",name:"Bezoekersparkeren",
      categoryId:null,categoryName:null,location:"OSS_J",isAvailable:true,
      isDefault:true,firstSeenAt:"2026-10-01T00:00:00Z",lastSeenAt:"2026-10-09T00:00:00Z"
    }]);
  }
  it("shows localized failures and completed progress counters",async()=>{
    setupSync();
    vi.mocked(getAdminProviderHistorySyncStatus).mockResolvedValue({
      states:[],
      runs:[{
        ...runningRun,
        status:"Failed",finishedAt:"2026-10-09T09:04:00Z",
        error:"Provider tijdelijk niet bereikbaar"
      }]
    });
    render(<AdminProviderHistoryPage/>);
    expect(await screen.findByText("Mislukt")).toBeTruthy();
    expect(screen.getByText("Provider tijdelijk niet bereikbaar")).toBeTruthy();
    expect(screen.getByText("6")).toBeTruthy();
    expect(screen.getByText("3")).toBeTruthy();
    expect(screen.getByText("1")).toBeTruthy();
    expect(screen.queryByRole("button",{name:"Annuleren"})).toBeNull();
  });
  it("starts a manual sync for the selected product",async()=>{
    setupSync();
    vi.mocked(startAdminProviderHistorySync).mockResolvedValue({
      id:"run-1",providerProductId:"visitor",status:"Running"
    });
    render(<AdminProviderHistoryPage/>);
    fireEvent.change(await screen.findByLabelText("Product voor synchronisatie"),{
      target:{value:"visitor"}
    });
    fireEvent.click(screen.getByRole("button",{name:"Synchroniseren"}));
    await waitFor(()=>expect(startAdminProviderHistorySync).toHaveBeenCalledWith("visitor"));
    await waitFor(()=>expect(getAdminProviderHistorySyncStatus).toHaveBeenCalledTimes(2));
  });
  it("offers cancellation for a running sync and refreshes its status",async()=>{
    setupSync();
    vi.mocked(getAdminProviderHistorySyncStatus)
      .mockResolvedValueOnce({states:[],runs:[runningRun]})
      .mockResolvedValue({states:[],runs:[{...runningRun,status:"Cancelled",finishedAt:"2026-10-09T09:01:00Z"}]});
    vi.mocked(cancelAdminProviderHistorySync).mockResolvedValue({
      runId:"run-1",status:"Cancelled"
    });
    render(<AdminProviderHistoryPage/>);
    fireEvent.click(await screen.findByRole("button",{name:"Annuleren"}));
    await waitFor(()=>expect(cancelAdminProviderHistorySync).toHaveBeenCalledWith("run-1"));
    await waitFor(()=>expect(screen.getByText("Geannuleerd")).toBeTruthy());
    expect(screen.queryByRole("button",{name:"Annuleren"})).toBeNull();
  });
});
