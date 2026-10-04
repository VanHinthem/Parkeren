// @vitest-environment happy-dom
import { cleanup,render,screen,waitFor } from "@testing-library/react";
import { afterEach,describe,expect,it,vi } from "vitest";
import {
  getAdminUserDetail,
  getVehicles,
  type AdminUserDetail
} from "../../api/client";
import { AdminUserDetailPage } from "./AdminUsersPage";

vi.mock("../../api/client",()=>({
  assignVehicle:vi.fn(),
  archiveUser:vi.fn(),
  archiveVehicle:vi.fn(),
  createUser:vi.fn(),
  createVehicle:vi.fn(),
  deleteUser:vi.fn(),
  deleteVehicle:vi.fn(),
  getAdminUserDetail:vi.fn(),
  getUsers:vi.fn(),
  getVehicles:vi.fn(),
  resetUserPin:vi.fn(),
  revokeUserSessions:vi.fn(),
  setAdminUserPolicy:vi.fn(),
  setUserActive:vi.fn(),
  setVehicleActive:vi.fn(),
  unassignVehicle:vi.fn()
}));

afterEach(()=>{
  cleanup();
  vi.resetAllMocks();
});

describe("AdminUserDetailPage lifecycle status",()=>{
  it("shows archived status and hides activation",async()=>{
    const detail:AdminUserDetail={
      user:{
        id:"user-1",
        username:"archived-visitor",
        role:"Visitor",
        status:"Archived",
        isActive:false,
        canDelete:false,
        maxConcurrentVisits:null
      },
      assignedVehicles:[],
      policy:{
        defaults:{
          maxPaidParkingDurationMinutes:240,
          maxVisitElapsedDurationMinutes:480,
          allowVisitExtension:true,
          allowOpenEndedVisits:false,
          maxConcurrentVisits:1
        },
        overrides:{
          maxPaidParkingDurationMode:"Inherit",
          maxPaidParkingDurationMinutes:null,
          maxVisitElapsedDurationMode:"Inherit",
          maxVisitElapsedDurationMinutes:null,
          allowVisitExtension:null,
          allowOpenEndedVisits:null,
          maxConcurrentVisits:null
        },
        effective:{
          maxPaidParkingDurationMinutes:240,
          maxVisitElapsedDurationMinutes:480,
          allowVisitExtension:true,
          allowOpenEndedVisits:false,
          maxConcurrentVisits:1
        },
        globalMaxConcurrentVisits:1
      },
      activeVisitCount:0
    };
    vi.mocked(getAdminUserDetail).mockResolvedValue(detail);
    vi.mocked(getVehicles).mockResolvedValue([]);

    render(<AdminUserDetailPage userId="user-1"/>);

    expect((await screen.findByText("Gearchiveerd")).textContent).toBe("Gearchiveerd");
    expect(screen.queryByRole("button",{name:"Activeren"})).toBeNull();
  });
});