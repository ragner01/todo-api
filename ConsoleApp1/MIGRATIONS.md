EF Core migrations — commands to run locally

Prerequisites
- .NET 8 SDK installed (`dotnet --version`)
- EF Core CLI tool: `dotnet tool update -g dotnet-ef`

Set working directory
- Change directory to the project folder: `ConsoleApp1/`

Create migrations
1) Initial schema (todos table):
   dotnet ef migrations add InitialCreate

2) Add labels/priority/soft-delete columns and query filter:
   dotnet ef migrations add AddPriorityLabelsSoftDelete

Apply to the database
- Update the SQLite database using the latest migration:
  dotnet ef database update

Notes
- The app also runs `db.Database.MigrateAsync()` on startup in Development, so the database will auto-create/update if migrations exist.
- If you need to reset the DB during development, stop the app, delete `ConsoleApp1/app.db`, and run `dotnet ef database update` again.
- If a migration was created with unintended changes, remove the last migration:
  dotnet ef migrations remove

Troubleshooting
- If `dotnet ef` fails to find the DbContext, ensure `ConsoleApp1/AppDbContextFactory.cs` is present (it is) and you’re in the `ConsoleApp1/` folder.
- If packages are missing, run: `dotnet restore`.

