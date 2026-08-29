using Microsoft.EntityFrameworkCore;
using QrBancoEconomico.Domain;

namespace QrBancoEconomico.Infrastructure;

public sealed class BanecoDbContext(DbContextOptions<BanecoDbContext> options) : DbContext(options)
{
    public DbSet<QrTransaction> QrTransactions => Set<QrTransaction>();
    public DbSet<QrPayment> QrPayments => Set<QrPayment>();
    public DbSet<BatchUpload> BatchUploads => Set<BatchUpload>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<QrTransaction>(e =>
        {
            e.ToTable("QrTransactions"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.MerchantTransactionId).IsUnique(); e.HasIndex(x => x.BankQrId).IsUnique().HasFilter("[BankQrId] IS NOT NULL");
            e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.MerchantTransactionId).HasMaxLength(100); e.Property(x => x.AccountCreditEncrypted).HasMaxLength(1024);
            e.HasMany(x => x.Payments).WithOne(x => x.QrTransaction).HasForeignKey(x => x.QrTransactionId);
        });
        model.Entity<QrPayment>(e => { e.ToTable("QrPayments"); e.HasKey(x => x.Id); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.QrId).HasMaxLength(100); });
        model.Entity<BatchUpload>(e => { e.ToTable("BatchUploads"); e.HasKey(x => x.Id); e.HasIndex(x => x.BatchId).IsUnique(); e.Property(x => x.Amount).HasPrecision(18, 2); });
    }
}
