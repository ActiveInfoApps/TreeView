using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiskSpaceTree.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Executions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RootPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalDirectoriesFound = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalFilesProcessed = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Executions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Directories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    ParentDirectoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FirstSeenExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastSeenExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAtExecutionId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Directories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Directories_Directories_ParentDirectoryId",
                        column: x => x.ParentDirectoryId,
                        principalTable: "Directories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Directories_Executions_DeletedAtExecutionId",
                        column: x => x.DeletedAtExecutionId,
                        principalTable: "Executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Directories_Executions_FirstSeenExecutionId",
                        column: x => x.FirstSeenExecutionId,
                        principalTable: "Executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Directories_Executions_LastSeenExecutionId",
                        column: x => x.LastSeenExecutionId,
                        principalTable: "Executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DirectoryInfos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DirectoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SizeInKb = table.Column<long>(type: "INTEGER", nullable: false),
                    FileCount = table.Column<long>(type: "INTEGER", nullable: false),
                    DirectSizeInKb = table.Column<long>(type: "INTEGER", nullable: false),
                    DirectFileCount = table.Column<long>(type: "INTEGER", nullable: false),
                    HasError = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeletedSnapshot = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DirectoryInfos_Directories_DirectoryId",
                        column: x => x.DirectoryId,
                        principalTable: "Directories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DirectoryInfos_Executions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "Executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Directories_DeletedAtExecutionId",
                table: "Directories",
                column: "DeletedAtExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Directories_FirstSeenExecutionId",
                table: "Directories",
                column: "FirstSeenExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Directories_LastSeenExecutionId",
                table: "Directories",
                column: "LastSeenExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Directories_ParentDirectoryId",
                table: "Directories",
                column: "ParentDirectoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Directories_Path",
                table: "Directories",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryInfos_DirectoryId_ExecutionId",
                table: "DirectoryInfos",
                columns: new[] { "DirectoryId", "ExecutionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryInfos_ExecutionId",
                table: "DirectoryInfos",
                column: "ExecutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectoryInfos");

            migrationBuilder.DropTable(
                name: "Directories");

            migrationBuilder.DropTable(
                name: "Executions");
        }
    }
}
