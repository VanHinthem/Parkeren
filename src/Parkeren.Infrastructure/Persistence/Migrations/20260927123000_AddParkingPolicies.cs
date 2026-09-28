using Microsoft.EntityFrameworkCore.Infrastructure;using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Parkeren.Infrastructure.Persistence.Migrations;
[DbContext(typeof(ParkerenDbContext))][Migration("20260927123000_AddParkingPolicies")]
public sealed class AddParkingPolicies:Migration{
protected override void Up(MigrationBuilder m){
m.CreateTable(name:"default_parking_policy",columns:t=>new{Id=t.Column<Guid>(type:"uuid",nullable:false),MaxPaidParkingDuration=t.Column<TimeSpan>(type:"interval",nullable:false),MaxVisitElapsedDuration=t.Column<TimeSpan>(type:"interval",nullable:true),AllowAutoExtension=t.Column<bool>(type:"boolean",nullable:false),MaxConcurrentVisits=t.Column<int>(type:"integer",nullable:false),UpdatedAt=t.Column<DateTimeOffset>(type:"timestamp with time zone",nullable:false)},constraints:t=>t.PrimaryKey("PK_default_parking_policy",x=>x.Id));
m.CreateTable(name:"user_policy_overrides",columns:t=>new{UserId=t.Column<Guid>(type:"uuid",nullable:false),MaxPaidParkingDuration=t.Column<TimeSpan>(type:"interval",nullable:true),MaxVisitElapsedDuration=t.Column<TimeSpan>(type:"interval",nullable:true),AllowAutoExtension=t.Column<bool>(type:"boolean",nullable:true),MaxConcurrentVisits=t.Column<int>(type:"integer",nullable:true),UpdatedAt=t.Column<DateTimeOffset>(type:"timestamp with time zone",nullable:false)},constraints:t=>{t.PrimaryKey("PK_user_policy_overrides",x=>x.UserId);t.ForeignKey("FK_user_policy_overrides_users_UserId",x=>x.UserId,"users","Id",onDelete:ReferentialAction.Restrict);});}
protected override void Down(MigrationBuilder m){m.DropTable("user_policy_overrides");m.DropTable("default_parking_policy");}}
