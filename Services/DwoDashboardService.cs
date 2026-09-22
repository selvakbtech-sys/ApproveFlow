namespace ApproveFlow.MVC.Services;

using ApproveFlow.MVC.Models.Dwo;

// The DWO module's provider for the Common Dashboard's DWO section.
//
// Registered in Program.cs against IDwoDashboardService. It only translates the caller's
// identity into a DwoActor — the same shape DwoController builds for its own pages — and
// hands off to DwoService, so the dashboard's scoping and counting live in one place with
// the grids the cards open.
public class DwoDashboardService : IDwoDashboardService
{
    private readonly IDwoService _dwo;
    public DwoDashboardService(IDwoService dwo) { _dwo = dwo; }

    public async Task<DwoDashboardVM> GetDashboardAsync(DwoUserContext ctx, CancellationToken ct = default)
    {
        // Every card opens a DWO Register grid, which is gated on "DWO Register". A user
        // without it gets no counts (the cards render "—") rather than numbers whose links
        // would land on Access Denied.
        if (!ctx.CanViewRegister) return new DwoDashboardVM();

        var actor = await _dwo.BuildActorAsync(new DwoActor
        {
            UserId       = ctx.UserId,
            IsManagement = ctx.IsManagement,
            IsHod        = ctx.IsHod,
        });
        return await _dwo.GetCommonDashboardAsync(actor);
    }
}
