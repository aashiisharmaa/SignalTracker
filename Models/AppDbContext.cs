

using SignalTracker.Models;
using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {

    }
    public DbSet<tbl_user> tbl_user { get; set; }
    public DbSet<m_user_type> m_user_type { get; set; }
    public DbSet<tbl_user_login_audit_details> tbl_user_login_audit_details { get; set; }
    public DbSet<m_email_setting> m_email_setting { get; set; }
    public DbSet<exception_history> exception_history { get; set; }
    public DbSet<tbl_session> tbl_session { get; set; }

    public DbSet<SessionWithUserDTO> SessionWithUserDTO { get; set; }

    public DbSet<PredictionPointDto> PredictionPointDto { get; set; }

    
    public DbSet<tbl_network_log> tbl_network_log { get; set; }

    public DbSet<thresholds> thresholds { get; set; }

    public DbSet<PolygonMatch> PolygonMatches { get; set; }
    public DbSet<map_regions> map_regions { get; set; }
    public DbSet<tbl_upload_history> tbl_upload_history { get; set; }
    public DbSet<tbl_prediction_data> tbl_prediction_data { get; set; }
    public DbSet<tbl_project> tbl_project { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PolygonDto>().HasNoKey().ToView(null);
        modelBuilder.Entity<PredictionPointDto>().HasNoKey();

        modelBuilder.Entity<map_regions>().ToTable("map_regions");
        modelBuilder.Entity<map_regions>().Property(r => r.region).HasColumnType("geometry");

    }
}

