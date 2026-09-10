using Microsoft.EntityFrameworkCore;
using QrBancoEconomico.Domain;

namespace QrBancoEconomico.Infrastructure;

public sealed class BanecoDbContext(DbContextOptions<BanecoDbContext> options) : DbContext(options)
{
    public DbSet<QrTransaction> QrTransactions => Set<QrTransaction>();
    public DbSet<QrPayment> QrPayments => Set<QrPayment>();
    public DbSet<BatchUpload> BatchUploads => Set<BatchUpload>();
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();
    public DbSet<SubscriberApiKey> SubscriberApiKeys => Set<SubscriberApiKey>();
    public DbSet<BanecoAccount> BanecoAccounts => Set<BanecoAccount>();
    public DbSet<SubscriberBanecoAccount> SubscriberBanecoAccounts => Set<SubscriberBanecoAccount>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<QrTransaction>(e =>
        {
            e.ToTable("QrTransactions"); e.HasKey(x => x.Id);
            // El identificador de transacción solo es único dentro de cada suscriptor: dos canales
            // distintos pueden reutilizar la misma numeración sin colisionar.
            e.HasIndex(x => new { x.SubscriberId, x.MerchantTransactionId }).IsUnique();
            // Baneco exige transactionId único por cuenta: si dos suscriptores comparten una cuenta
            // (modo multiplexor), la colisión se detecta aquí y no en un rechazo del banco.
            e.HasIndex(x => new { x.BanecoAccountId, x.MerchantTransactionId }).IsUnique()
                .HasFilter("[BanecoAccountId] IS NOT NULL");
            e.HasIndex(x => x.BankQrId).IsUnique().HasFilter("[BankQrId] IS NOT NULL");
            e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.MerchantTransactionId).HasMaxLength(100); e.Property(x => x.AccountCreditEncrypted).HasMaxLength(1024);
            e.HasMany(x => x.Payments).WithOne(x => x.QrTransaction).HasForeignKey(x => x.QrTransactionId);
            e.HasOne(x => x.Subscriber).WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BanecoAccount).WithMany().HasForeignKey(x => x.BanecoAccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<QrPayment>(e => { e.ToTable("QrPayments"); e.HasKey(x => x.Id); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.QrId).HasMaxLength(100); });
        model.Entity<BatchUpload>(e =>
        {
            e.ToTable("BatchUploads"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SubscriberId, x.BatchId }).IsUnique();
            e.HasIndex(x => new { x.BanecoAccountId, x.BatchId }).IsUnique().HasFilter("[BanecoAccountId] IS NOT NULL");
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.Subscriber).WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BanecoAccount).WithMany().HasForeignKey(x => x.BanecoAccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Subscriber>(e =>
        {
            e.ToTable("Subscribers"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ContactEmail).HasMaxLength(200);
            // Sobre AES-GCM en Base64 alrededor del criptograma de Baneco.
            e.Property(x => x.SavingsAccountEncrypted).HasMaxLength(1024);
            e.HasMany(x => x.ApiKeys).WithOne(x => x.Subscriber).HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<SubscriberApiKey>(e =>
        {
            e.ToTable("SubscriberApiKeys"); e.HasKey(x => x.Id);
            // Único e indexado: la autenticación resuelve la fila por este valor en cada solicitud.
            e.HasIndex(x => x.ApiKey).IsUnique();
            e.Property(x => x.Environment).HasMaxLength(16);
            e.Property(x => x.Scopes).HasMaxLength(512);
            e.Property(x => x.Label).HasMaxLength(100);
        });
        model.Entity<BanecoAccount>(e =>
        {
            e.ToTable("BanecoAccounts"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.CredentialRef).HasMaxLength(100);
            e.Property(x => x.AccountCodeEncrypted).HasMaxLength(1024);
            e.Property(x => x.BatchDebitAccountEncrypted).HasMaxLength(1024);
            // Sobre AES-GCM en Base64; el usuario cabe de sobra en 512 y la contraseña en 1024.
            e.Property(x => x.AuthUserNameEncrypted).HasMaxLength(512);
            e.Property(x => x.AuthPasswordEncrypted).HasMaxLength(1024);
        });
        model.Entity<SubscriberBanecoAccount>(e =>
        {
            e.ToTable("SubscriberBanecoAccounts");
            e.HasKey(x => new { x.SubscriberId, x.BanecoAccountId });
            // Como máximo una cuenta predeterminada por suscriptor.
            e.HasIndex(x => x.SubscriberId).IsUnique().HasFilter("[IsDefault] = 1").HasDatabaseName("UX_SubscriberBanecoAccounts_Default");
            e.Property(x => x.GrantedBy).HasMaxLength(50);
            e.HasOne(x => x.Subscriber).WithMany(x => x.Accounts).HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.BanecoAccount).WithMany(x => x.Subscribers).HasForeignKey(x => x.BanecoAccountId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
