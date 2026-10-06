using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoveryEmailSelectionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DestinationFingerprint",
                table: "RecoveryResetApprovals",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationKind",
                table: "RecoveryResetApprovals",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DestinationVersion",
                table: "RecoveryResetApprovals",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SelectionEpoch",
                table: "RecoveryResetApprovals",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RecoveryEmailId",
                table: "RecoveryProofChallenges",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "DeliveryState",
                table: "RecoveryProofChallenges",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationFingerprint",
                table: "RecoveryProofChallenges",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationKind",
                table: "RecoveryProofChallenges",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DestinationVersion",
                table: "RecoveryProofChallenges",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SelectionEpoch",
                table: "RecoveryProofChallenges",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provenance",
                table: "RecoveryEmails",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "LegacyUnknown");

            migrationBuilder.AlterColumn<Guid>(
                name: "RecoveryEmailId",
                table: "NativeRecoveryResetApprovals",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "DestinationFingerprint",
                table: "NativeRecoveryResetApprovals",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationKind",
                table: "NativeRecoveryResetApprovals",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DestinationVersion",
                table: "NativeRecoveryResetApprovals",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SelectionEpoch",
                table: "NativeRecoveryResetApprovals",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecoveryEmailPreferences",
                columns: table => new
                {
                    LocalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SelectionEpoch = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceDefaultOptOutAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AdministrativeBlockedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryEmailPreferences", x => x.LocalAccountId);
                    table.ForeignKey(
                        name: "FK_RecoveryEmailPreferences_AspNetUsers_LocalAccountId",
                        column: x => x.LocalAccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SelectionEpoch = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AbandonedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryNotifications_AspNetUsers_LocalAccountId",
                        column: x => x.LocalAccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryPrecheckGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderBindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderBindingVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContextHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CsrfHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EffectivePolicyVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SelectionEpoch = table.Column<long>(type: "bigint", nullable: false),
                    DestinationKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DestinationFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DestinationVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReservedChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryPrecheckGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryPrecheckGrants_AspNetUsers_LocalAccountId",
                        column: x => x.LocalAccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecoveryPrecheckGrants_ProviderSubjectDirectoryBindings_ProviderBindingId",
                        column: x => x.ProviderBindingId,
                        principalTable: "ProviderSubjectDirectoryBindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecoveryPrecheckGrants_RecoveryProofChallenges_ReservedChallengeId",
                        column: x => x.ReservedChallengeId,
                        principalTable: "RecoveryProofChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryStepUpGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SecurityStamp = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContextHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CsrfHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AuthorityBinding = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AuthenticatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryStepUpGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryStepUpGrants_AspNetUsers_LocalAccountId",
                        column: x => x.LocalAccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryThrottleBuckets",
                columns: table => new
                {
                    PartitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WindowStartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryThrottleBuckets", x => x.PartitionHash);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryEmailChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    NormalizedAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ExpectedSelectionEpoch = table.Column<long>(type: "bigint", nullable: false),
                    SecurityStamp = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContextHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CsrfHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StepUpGrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextSendAllowedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerificationAttempts = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryEmailChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryEmailChangeRequests_AspNetUsers_LocalAccountId",
                        column: x => x.LocalAccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecoveryEmailChangeRequests_RecoveryStepUpGrants_StepUpGrantId",
                        column: x => x.StepUpGrantId,
                        principalTable: "RecoveryStepUpGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryEmailChangeRequests_ExpiresAtUtc",
                table: "RecoveryEmailChangeRequests",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryEmailChangeRequests_LocalAccountId",
                table: "RecoveryEmailChangeRequests",
                column: "LocalAccountId",
                unique: true,
                filter: "[ConsumedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryEmailChangeRequests_StepUpGrantId",
                table: "RecoveryEmailChangeRequests",
                column: "StepUpGrantId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryNotifications_DeliveredAtUtc_AbandonedAtUtc_NextAttemptAtUtc",
                table: "RecoveryNotifications",
                columns: new[] { "DeliveredAtUtc", "AbandonedAtUtc", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryNotifications_LocalAccountId_SelectionEpoch_Kind",
                table: "RecoveryNotifications",
                columns: new[] { "LocalAccountId", "SelectionEpoch", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryPrecheckGrants_LocalAccountId_ExpiresAtUtc",
                table: "RecoveryPrecheckGrants",
                columns: new[] { "LocalAccountId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryPrecheckGrants_ProviderBindingId",
                table: "RecoveryPrecheckGrants",
                column: "ProviderBindingId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryPrecheckGrants_ReservedChallengeId",
                table: "RecoveryPrecheckGrants",
                column: "ReservedChallengeId",
                unique: true,
                filter: "[ReservedChallengeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryStepUpGrants_LocalAccountId_ExpiresAtUtc",
                table: "RecoveryStepUpGrants",
                columns: new[] { "LocalAccountId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryThrottleBuckets_ExpiresAtUtc",
                table: "RecoveryThrottleBuckets",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never erase effective choices, tombstones, pending audit or default-bound proof on rollback.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM "RecoveryEmailPreferences") OR
                    EXISTS (SELECT 1 FROM "RecoveryEmailChangeRequests") OR
                    EXISTS (SELECT 1 FROM "RecoveryPrecheckGrants") OR
                    EXISTS (SELECT 1 FROM "RecoveryStepUpGrants") OR
                    EXISTS (SELECT 1 FROM "RecoveryNotifications") OR
                    EXISTS (SELECT 1 FROM "RecoveryThrottleBuckets") OR
                    EXISTS (SELECT 1 FROM "RecoveryEmails" WHERE "Provenance" <> 'LegacyUnknown') OR
                    EXISTS (SELECT 1 FROM "RecoveryProofChallenges" WHERE "SelectionEpoch" IS NOT NULL) OR
                    EXISTS (SELECT 1 FROM "RecoveryResetApprovals" WHERE "SelectionEpoch" IS NOT NULL) OR
                    EXISTS (SELECT 1 FROM "NativeRecoveryResetApprovals" WHERE "SelectionEpoch" IS NOT NULL) OR
                    EXISTS (SELECT 1 FROM "RecoveryProofChallenges" WHERE "RecoveryEmailId" IS NULL) OR
                    EXISTS (SELECT 1 FROM "NativeRecoveryResetApprovals" WHERE "RecoveryEmailId" IS NULL)
                    THROW 51000, 'Recovery selection state requires an explicit archival and compatibility decision before downgrade.', 1;
                """);

            migrationBuilder.DropTable(
                name: "RecoveryEmailChangeRequests");

            migrationBuilder.DropTable(
                name: "RecoveryEmailPreferences");

            migrationBuilder.DropTable(
                name: "RecoveryNotifications");

            migrationBuilder.DropTable(
                name: "RecoveryPrecheckGrants");

            migrationBuilder.DropTable(
                name: "RecoveryThrottleBuckets");

            migrationBuilder.DropTable(
                name: "RecoveryStepUpGrants");

            migrationBuilder.DropColumn(
                name: "DestinationFingerprint",
                table: "RecoveryResetApprovals");

            migrationBuilder.DropColumn(
                name: "DestinationKind",
                table: "RecoveryResetApprovals");

            migrationBuilder.DropColumn(
                name: "DestinationVersion",
                table: "RecoveryResetApprovals");

            migrationBuilder.DropColumn(
                name: "SelectionEpoch",
                table: "RecoveryResetApprovals");

            migrationBuilder.DropColumn(
                name: "DeliveryState",
                table: "RecoveryProofChallenges");

            migrationBuilder.DropColumn(
                name: "DestinationFingerprint",
                table: "RecoveryProofChallenges");

            migrationBuilder.DropColumn(
                name: "DestinationKind",
                table: "RecoveryProofChallenges");

            migrationBuilder.DropColumn(
                name: "DestinationVersion",
                table: "RecoveryProofChallenges");

            migrationBuilder.DropColumn(
                name: "SelectionEpoch",
                table: "RecoveryProofChallenges");

            migrationBuilder.DropColumn(
                name: "Provenance",
                table: "RecoveryEmails");

            migrationBuilder.DropColumn(
                name: "DestinationFingerprint",
                table: "NativeRecoveryResetApprovals");

            migrationBuilder.DropColumn(
                name: "DestinationKind",
                table: "NativeRecoveryResetApprovals");

            migrationBuilder.DropColumn(
                name: "DestinationVersion",
                table: "NativeRecoveryResetApprovals");

            migrationBuilder.DropColumn(
                name: "SelectionEpoch",
                table: "NativeRecoveryResetApprovals");

            migrationBuilder.AlterColumn<Guid>(
                name: "RecoveryEmailId",
                table: "RecoveryProofChallenges",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RecoveryEmailId",
                table: "NativeRecoveryResetApprovals",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
