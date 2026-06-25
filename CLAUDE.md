# ApproveFlow Project Rules

## Technology
- ASP.NET Core MVC
- Entity Framework Core
- SQL Server
- Bootstrap 5

## Coding Rules
- Do not modify existing working functionality.
- Always preserve current UI unless explicitly requested.
- Follow existing coding style.
- Use async/await for database operations.
- Reuse existing services and repositories.

## Dashboard Rules
- My Requests = logged-in user's requests only.
- My Approvals = requests assigned to logged-in approver.
- My Territory = requests in user's department/location.
- KPI cards and tables must use the same datasource.

## Workflow Rules
- Approval dropdown must show only roles with approval rights.
- Prevent removing approval rights if role is used in active workflows.
- Maximum approval levels = 3.

## UI Rules
- Maintain current theme and styling.
- Keep responsive design.
- Use existing Bootstrap components.

## Before Making Changes
- First identify affected files.
- Explain proposed changes.
- Then implement changes.

## Database Rules
- Never modify database schema without confirmation.
- Always check existing migrations before creating new ones.
- Prefer updating services over direct controller database access.

## Dashboard Validation Rules
- KPI cards and detail tables must always use identical filters.
- Notification counts must match dashboard counts.
- User-specific dashboards must only show logged-in user data.
- Admin dashboard can show global data.

## Approval Rules
- Current approver must be calculated from CurrentStepLevel.
- Clarification counts must match notification counts.
- Approval chain must respect workflow level sequence.

## Code Changes
- Show affected files before making changes.
- Minimize changes to existing code.
- Fix root cause instead of hardcoding values.