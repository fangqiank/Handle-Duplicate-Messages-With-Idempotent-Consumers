using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;
using Microsoft.EntityFrameworkCore;

namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Data
{
    public class AppDbContext: DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        public DbSet<Order> Orders { get; set; }
        public DbSet<IdempotencyRecord> IdempotencyRecords { get; set; }
        public DbSet<DeadLetterMessage> DeadLetterMessages { get; set; }
        public DbSet<DuplicateAttempt> DuplicateAttempts { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<IdempotencyRecord>()
                .HasKey(pm => new
                {
                    pm.MessageId,
                    pm.ConsumerName
                });

            modelBuilder.Entity<Order>().HasKey(o => o.Id);
            modelBuilder.Entity<DeadLetterMessage>().HasKey(d => d.Id);
            modelBuilder.Entity<DuplicateAttempt>().HasKey(d => d.Id);
        }
    }
}
