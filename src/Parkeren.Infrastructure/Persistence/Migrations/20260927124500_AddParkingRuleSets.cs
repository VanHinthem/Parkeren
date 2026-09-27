using Microsoft.EntityFrameworkCore.Infrastructure;using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Parkeren.Infrastructure.Persistence.Migrations;
[DbContext(typeof(ParkerenDbContext))][Migration("20260927124500_AddParkingRuleSets")]
public sealed class AddParkingRuleSets:Migration{
protected override void Up(MigrationBuilder m){
m.CreateTable("parking_rule_sets",t=>new{Id=t.Column<Guid>("uuid",nullable:false),ValidFrom=t.Column<DateTimeOffset>("timestamp with time zone",nullable:false),ValidUntil=t.Column<DateTimeOffset>("timestamp with time zone",nullable:true),MaxProviderActionDuration=t.Column<TimeSpan>("interval",nullable:false)},constraints:t=>t.PrimaryKey("PK_parking_rule_sets",x=>x.Id));
m.CreateTable("paid_windows",t=>new{Id=t.Column<Guid>("uuid",nullable:false),ParkingRuleSetId=t.Column<Guid>("uuid",nullable:false),Day=t.Column<int>("integer",nullable:false),Start=t.Column<TimeOnly>("time without time zone",nullable:false),End=t.Column<TimeOnly>("time without time zone",nullable:false)},constraints:t=>{t.PrimaryKey("PK_paid_windows",x=>x.Id);t.ForeignKey("FK_paid_windows_parking_rule_sets_ParkingRuleSetId",x=>x.ParkingRuleSetId,"parking_rule_sets","Id",onDelete:ReferentialAction.Cascade);});
m.CreateIndex("IX_paid_windows_ParkingRuleSetId_Day_Start_End","paid_windows",new[]{"ParkingRuleSetId","Day","Start","End"},unique:true);}
protected override void Down(MigrationBuilder m){m.DropTable("paid_windows");m.DropTable("parking_rule_sets");}}
