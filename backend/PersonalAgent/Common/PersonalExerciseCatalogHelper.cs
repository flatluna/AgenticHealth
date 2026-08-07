using Microsoft.EntityFrameworkCore;
using PersonalAgent.Data;

namespace PersonalAgent.Common;

/// <summary>
/// Shared find-or-create logic for a person's personal exercise catalog (Data/PersonalExercise.cs),
/// used both by ExerciseAgent's "log_exercise" chat tool and ExerciseFunction's custom-save
/// endpoint so a "caminé 40 min" reported via chat and one saved via the "Crea tu propio
/// ejercicio" form both land in the same reusable per-person catalog.
/// </summary>
public static class PersonalExerciseCatalogHelper
{
    public static async Task<PersonalExercise> FindOrCreateAsync(
        PersonalAgentDbContext db,
        int personId,
        string name,
        int durationMinutes,
        double? caloriesBurned,
        CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var item = await db.PersonalExercises
            .FirstOrDefaultAsync(pe => pe.PersonId == personId && pe.NormalizedName == normalized, cancellationToken);

        if (item is null)
        {
            item = new PersonalExercise
            {
                PersonId = personId,
                Name = name.Trim(),
                NormalizedName = normalized,
                DurationMinutes = durationMinutes,
                CaloriesBurned = caloriesBurned,
            };
            db.PersonalExercises.Add(item);
        }
        else
        {
            // Keep the catalog's reference duration/calories fresh with the latest estimate.
            item.DurationMinutes = durationMinutes;
            item.CaloriesBurned = caloriesBurned;
        }

        item.TimesLogged++;
        return item;
    }
}
