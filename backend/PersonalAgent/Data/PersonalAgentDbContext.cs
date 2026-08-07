using Microsoft.EntityFrameworkCore;

namespace PersonalAgent.Data;

public sealed class PersonalAgentDbContext : DbContext
{
    public PersonalAgentDbContext(DbContextOptions<PersonalAgentDbContext> options) : base(options)
    {
    }

    public DbSet<Person> People => Set<Person>();

    public DbSet<WeightLog> WeightLogs => Set<WeightLog>();

    public DbSet<MealLog> MealLogs => Set<MealLog>();

    public DbSet<ExerciseLog> ExerciseLogs => Set<ExerciseLog>();

    public DbSet<Goal> Goals => Set<Goal>();

    public DbSet<GoalPlan> GoalPlans => Set<GoalPlan>();

    public DbSet<GoalPlanCheckIn> GoalPlanCheckIns => Set<GoalPlanCheckIn>();

    public DbSet<AppUser> AppUsers => Set<AppUser>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<FoodItem> FoodItems => Set<FoodItem>();

    public DbSet<PersonalExercise> PersonalExercises => Set<PersonalExercise>();

    public DbSet<PersonalFoodItem> PersonalFoodItems => Set<PersonalFoodItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>()
            .HasMany(p => p.WeightLogs)
            .WithOne(w => w.Person)
            .HasForeignKey(w => w.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Person>()
            .HasMany(p => p.MealLogs)
            .WithOne(m => m.Person)
            .HasForeignKey(m => m.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Person>()
            .HasMany(p => p.ExerciseLogs)
            .WithOne(e => e.Person)
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Person>()
            .HasMany(p => p.Goals)
            .WithOne(g => g.Person)
            .HasForeignKey(g => g.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Person>()
            .HasMany(p => p.GoalPlans)
            .WithOne(gp => gp.Person)
            .HasForeignKey(gp => gp.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Person>()
            .Property(p => p.ActivityLevel)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<Goal>()
            .Property(g => g.Type)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<Goal>()
            .Property(g => g.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<GoalPlan>()
            .Property(gp => gp.ActivityLevel)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<GoalPlan>()
            .HasMany(gp => gp.CheckIns)
            .WithOne(c => c.GoalPlan)
            .HasForeignKey(c => c.GoalPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GoalPlanCheckIn>()
            .HasIndex(c => new { c.GoalPlanId, c.CheckInDate })
            .IsUnique();

        modelBuilder.Entity<GoalPlanCheckIn>()
            .HasOne(c => c.Person)
            .WithMany()
            .HasForeignKey(c => c.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.AzureObjectId)
            .IsUnique();

        // Filtered unique index: SQL Server treats multiple NULLs as distinct by default
        // for a plain unique index, which is exactly what we want here (the single legacy
        // "Usuario" row has a null AzureObjectId; every real account gets a non-null,
        // unique value so its health data stays isolated from every other account).
        modelBuilder.Entity<Person>()
            .HasIndex(p => p.AzureObjectId)
            .IsUnique();

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<AppUser>()
            .HasOne(u => u.Profile)
            .WithOne(p => p.AppUser)
            .HasForeignKey<UserProfile>(p => p.AppUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserProfile>()
            .HasIndex(p => p.AppUserId)
            .IsUnique();

        modelBuilder.Entity<MealLog>()
            .Property(m => m.MealType)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<MealLog>()
            .HasOne(m => m.FoodItem)
            .WithMany()
            .HasForeignKey(m => m.FoodItemId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<FoodItem>()
            .HasIndex(f => f.MatchKey)
            .IsUnique();

        modelBuilder.Entity<Person>()
            .HasMany(p => p.PersonalExercises)
            .WithOne(pe => pe.Person)
            .HasForeignKey(pe => pe.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PersonalExercise>()
            .HasIndex(pe => new { pe.PersonId, pe.NormalizedName })
            .IsUnique();

        modelBuilder.Entity<ExerciseLog>()
            .HasOne(e => e.PersonalExercise)
            .WithMany()
            .HasForeignKey(e => e.PersonalExerciseId)
            // ClientSetNull (not SetNull) avoids a "multiple cascade paths" SQL Server error,
            // since Person already cascade-deletes ExerciseLogs directly.
            .OnDelete(DeleteBehavior.ClientSetNull);

        modelBuilder.Entity<Person>()
            .HasMany(p => p.PersonalFoodItems)
            .WithOne(pf => pf.Person)
            .HasForeignKey(pf => pf.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PersonalFoodItem>()
            .HasIndex(pf => new { pf.PersonId, pf.NormalizedName })
            .IsUnique();

        modelBuilder.Entity<MealLog>()
            .HasOne(m => m.PersonalFoodItem)
            .WithMany()
            .HasForeignKey(m => m.PersonalFoodItemId)
            // ClientSetNull (not SetNull) avoids a "multiple cascade paths" SQL Server error,
            // since Person already cascade-deletes MealLogs directly.
            .OnDelete(DeleteBehavior.ClientSetNull);
    }
}
