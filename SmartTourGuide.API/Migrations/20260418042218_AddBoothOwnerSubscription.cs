using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourGuide.API.Migrations
{
    /// <inheritdoc />
    public partial class AddBoothOwnerSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS `SubscriptionPlans` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
    `PriceMonthly` decimal(18,2) NOT NULL,
    `PriceYearly` decimal(18,2) NOT NULL,
    `MaxActivePois` int NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_SubscriptionPlans` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;
");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS `BoothOwnerSubscriptions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OwnerId` int NOT NULL,
    `PlanId` int NOT NULL,
    `BillingCycle` int NOT NULL,
    `StartDate` datetime(6) NOT NULL,
    `EndDate` datetime(6) NOT NULL,
    `Status` int NOT NULL,
    `AmountPaid` decimal(18,2) NOT NULL,
    `PaymentReference` varchar(200) CHARACTER SET utf8mb4 NULL,
    `PaymentMethod` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `CancelReason` varchar(500) CHARACTER SET utf8mb4 NULL,
    `PlanNameSnapshot` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK_BoothOwnerSubscriptions` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_BoothOwnerSubscriptions_SubscriptionPlans_PlanId` FOREIGN KEY (`PlanId`) REFERENCES `SubscriptionPlans` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_BoothOwnerSubscriptions_Users_OwnerId` FOREIGN KEY (`OwnerId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE,
    KEY `IX_BoothOwnerSubscriptions_EndDate` (`EndDate`),
    KEY `IX_BoothOwnerSubscriptions_OwnerId_Status` (`OwnerId`, `Status`),
    KEY `IX_BoothOwnerSubscriptions_PlanId` (`PlanId`)
) CHARACTER SET=utf8mb4;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS `BoothOwnerSubscriptions`;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS `SubscriptionPlans`;");
        }
    }
}
