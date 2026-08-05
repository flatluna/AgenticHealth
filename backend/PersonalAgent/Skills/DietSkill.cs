using System.Text.Json.Serialization;

namespace PersonalAgent.Skills;

/// <summary>
/// "Skill" pattern for DietAgent - same lightweight approach HumanOS uses instead of the
/// real Agent Framework Skills Harness: a plain enum + a static guidance-text lookup +
/// a selector that decides which skill applies, spliced into the prompt as extra context.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DietSkill
{
    CalorieCounting,
    WeightLoss,
    MuscleGain,
    GeneralNutrition,
}

public static class DietSkillLibrary
{
    public static string InstructionsFor(DietSkill skill) => skill switch
    {
        DietSkill.CalorieCounting =>
            "El usuario quiere contar calorías o conocer el valor nutricional de un alimento. " +
            "Usa la herramienta de búsqueda de alimentos para obtener datos reales antes de responder. " +
            "Presenta calorías, y si están disponibles, proteínas/carbohidratos/grasas por porción.",

        DietSkill.WeightLoss =>
            "El usuario busca perder peso. Enfócate en déficit calórico sostenible, porciones " +
            "razonables y hábitos a largo plazo. Evita recomendar dietas extremas o restrictivas.",

        DietSkill.MuscleGain =>
            "El usuario busca ganar masa muscular. Enfócate en superávit calórico moderado y " +
            "suficiente ingesta de proteína, junto con la recomendación de acompañarlo de entrenamiento.",

        DietSkill.GeneralNutrition =>
            "El usuario tiene una pregunta general de nutrición. Da una respuesta balanceada " +
            "basada en evidencia, sin asumir un objetivo específico de peso.",

        _ => throw new ArgumentOutOfRangeException(nameof(skill), skill, null),
    };
}

/// <summary>
/// Very small keyword-based selector - decided by the caller/service layer (not the LLM),
/// mirroring HumanOS's Runtime-side TutorSkillSelector pattern.
/// </summary>
public static class DietSkillSelector
{
    public static DietSkill Select(string userMessage)
    {
        var text = userMessage.ToLowerInvariant();

        if (ContainsAny(text, "caloría", "calorias", "calorí", "kcal"))
        {
            return DietSkill.CalorieCounting;
        }

        if (ContainsAny(text, "bajar de peso", "perder peso", "adelgazar", "déficit"))
        {
            return DietSkill.WeightLoss;
        }

        if (ContainsAny(text, "ganar masa", "masa muscular", "volumen", "hipertrofia"))
        {
            return DietSkill.MuscleGain;
        }

        return DietSkill.GeneralNutrition;
    }

    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(text.Contains);
}
