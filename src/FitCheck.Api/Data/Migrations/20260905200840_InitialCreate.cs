using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Handle = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    HandleLower = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AccountType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Bio = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AvatarPath = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    AvatarVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Interests = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Confirmed16Plus = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreferredLanguage = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StreakCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastCheckDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Suspended = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Challenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BrandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Brief = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Intent = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Prize = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PrizeUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    EndsAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WinnerPostId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Challenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Challenges_Users_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Checks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Intent = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Occasion = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    VideoPath = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    FeedbackJson = table.Column<string>(type: "TEXT", nullable: true),
                    PromptVersion = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Checks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Checks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Follows",
                columns: table => new
                {
                    FollowerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FollowedId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Follows", x => new { x.FollowerId, x.FollowedId });
                    table.ForeignKey(
                        name: "FK_Follows_Users_FollowedId",
                        column: x => x.FollowedId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Follows_Users_FollowerId",
                        column: x => x.FollowerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ActorHandle = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChallengeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    P256dh = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Intent = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    IntentMatch = table.Column<int>(type: "INTEGER", nullable: false),
                    Headline = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Caption = table.Column<string>(type: "TEXT", maxLength: 140, nullable: true),
                    ChallengeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FeaturedByBrandId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FeaturedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FireCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CommentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReportCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Hidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Posts_Challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "Challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Posts_Checks_CheckId",
                        column: x => x.CheckId,
                        principalTable: "Checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Posts_Users_FeaturedByBrandId",
                        column: x => x.FeaturedByBrandId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Posts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChallengeVotes",
                columns: table => new
                {
                    ChallengeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChallengeVotes", x => new { x.ChallengeId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ChallengeVotes_Challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "Challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChallengeVotes_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChallengeVotes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ReportCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Hidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Fires",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fires", x => new { x.PostId, x.UserId });
                    table.ForeignKey(
                        name: "FK_Fires_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Fires_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostMentions",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostMentions", x => new { x.PostId, x.UserId });
                    table.ForeignKey(
                        name: "FK_PostMentions_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostMentions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostTags",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostTags", x => new { x.PostId, x.Tag });
                    table.ForeignKey(
                        name: "FK_PostTags_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Price = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductLinks_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedPosts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedPosts", x => new { x.UserId, x.PostId });
                    table.ForeignKey(
                        name: "FK_SavedPosts_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SavedPosts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CommentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReporterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reports_Comments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reports_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reports_Users_ReporterId",
                        column: x => x.ReporterId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_BrandId",
                table: "Challenges",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_EndsAt",
                table: "Challenges",
                column: "EndsAt");

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_Tag",
                table: "Challenges",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeVotes_ChallengeId_PostId",
                table: "ChallengeVotes",
                columns: new[] { "ChallengeId", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeVotes_PostId",
                table: "ChallengeVotes",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeVotes_UserId",
                table: "ChallengeVotes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Checks_UserId_CreatedAt",
                table: "Checks",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_PostId_CreatedAt",
                table: "Comments",
                columns: new[] { "PostId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_UserId",
                table: "Comments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Fires_UserId",
                table: "Fires",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Follows_FollowedId",
                table: "Follows",
                column: "FollowedId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PostMentions_UserId",
                table: "PostMentions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_ChallengeId",
                table: "Posts",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_ChallengeId_UserId",
                table: "Posts",
                columns: new[] { "ChallengeId", "UserId" },
                unique: true,
                filter: "\"ChallengeId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CheckId",
                table: "Posts",
                column: "CheckId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CreatedAt",
                table: "Posts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_FeaturedByBrandId",
                table: "Posts",
                column: "FeaturedByBrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_UserId_CreatedAt",
                table: "Posts",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PostTags_Tag_PostId",
                table: "PostTags",
                columns: new[] { "Tag", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductLinks_PostId",
                table: "ProductLinks",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_Endpoint",
                table: "PushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_UserId",
                table: "PushSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_CommentId_ReporterId",
                table: "Reports",
                columns: new[] { "CommentId", "ReporterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_PostId_ReporterId",
                table: "Reports",
                columns: new[] { "PostId", "ReporterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReporterId",
                table: "Reports",
                column: "ReporterId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedPosts_PostId",
                table: "SavedPosts",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_HandleLower",
                table: "Users",
                column: "HandleLower",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChallengeVotes");

            migrationBuilder.DropTable(
                name: "Fires");

            migrationBuilder.DropTable(
                name: "Follows");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PostMentions");

            migrationBuilder.DropTable(
                name: "PostTags");

            migrationBuilder.DropTable(
                name: "ProductLinks");

            migrationBuilder.DropTable(
                name: "PushSubscriptions");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "SavedPosts");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "Posts");

            migrationBuilder.DropTable(
                name: "Challenges");

            migrationBuilder.DropTable(
                name: "Checks");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
