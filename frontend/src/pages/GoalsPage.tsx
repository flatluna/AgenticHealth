import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { Target, Loader2, AlertCircle, Flame, Beef, Wheat, Droplet, Dumbbell, Apple, Flag, Lightbulb, Save } from 'lucide-react';
import {
  getGoalsProfile,
  saveGoalsProfile,
  createGoalPlan,
  getLatestGoalPlan,
  type ActivityLevel,
  type GoalPlan,
} from '../api/goalsApi';

const ACTIVITY_LEVEL_LABEL: Record<ActivityLevel, string> = {
  Sedentary: 'Sedentario (poco o ningún ejercicio)',
  Light: 'Ligero (1-3 días/semana)',
  Moderate: 'Moderado (3-5 días/semana)',
  Active: 'Activo (6-7 días/semana)',
  VeryActive: 'Muy activo (trabajo físico o 2x/día)',
};

function computeBmi(weightKg: number, heightCm: number): number | null {
  if (!weightKg || !heightCm) return null;
  const heightM = heightCm / 100;
  return Math.round((weightKg / (heightM * heightM)) * 10) / 10;
}

export function GoalsPage() {
  const [weightKg, setWeightKg] = useState('');
  const [heightCm, setHeightCm] = useState('');
  const [activityLevel, setActivityLevel] = useState<ActivityLevel>('Sedentary');
  const [goalsText, setGoalsText] = useState('');

  const [plan, setPlan] = useState<GoalPlan | null>(null);
  const [isLoadingInitial, setIsLoadingInitial] = useState(true);
  const [isGenerating, setIsGenerating] = useState(false);
  const [isSavingProfile, setIsSavingProfile] = useState(false);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        const [profile, latest] = await Promise.all([getGoalsProfile(), getLatestGoalPlan()]);
        if (profile.weightKg) setWeightKg(String(profile.weightKg));
        if (profile.heightCm) setHeightCm(String(profile.heightCm));
        if (profile.activityLevel) setActivityLevel(profile.activityLevel);
        if (latest.plan) {
          setPlan(latest.plan);
        }
      } catch {
        // Silent - the form still works from scratch if this fails (e.g. brand-new person).
      } finally {
        setIsLoadingInitial(false);
      }
    })();
  }, []);

  const previewBmi = useMemo(
    () => computeBmi(parseFloat(weightKg), parseFloat(heightCm)),
    [weightKg, heightCm],
  );

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const weight = parseFloat(weightKg);
    const height = parseFloat(heightCm);
    if (!weight || !height || !goalsText.trim() || isGenerating) return;

    setIsGenerating(true);
    setError(null);
    try {
      const response = await createGoalPlan({ weightKg: weight, heightCm: height, activityLevel, goalsText: goalsText.trim() });
      setPlan(response.plan);
    } catch (err) {
      const message =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'No se pudo generar el plan. Verifica que el backend esté corriendo.';
      setError(message);
    } finally {
      setIsGenerating(false);
    }
  };

  const handleSaveProfile = async () => {
    const weight = parseFloat(weightKg);
    const height = parseFloat(heightCm);
    if (!weight || !height || isSavingProfile) return;

    setIsSavingProfile(true);
    setSaveMessage(null);
    setError(null);
    try {
      await saveGoalsProfile({ weightKg: weight, heightCm: height, activityLevel });
      setSaveMessage('Cambios guardados.');
    } catch {
      setError('No se pudieron guardar los cambios.');
    } finally {
      setIsSavingProfile(false);
    }
  };

  return (
    <div className="h-full overflow-y-auto bg-[var(--app-bg)] p-6">
      <h2 className="text-xl font-semibold text-[var(--text-primary)]">Objetivos</h2>
      <p className="mt-1 text-sm text-[var(--text-muted)]">
        Cuéntale al agente tu estado actual y tus metas, y te generará un plan basado en evidencia.
      </p>

      {isLoadingInitial ? (
        <div className="mt-6 flex items-center gap-2 text-sm text-[var(--text-muted)]">
          <Loader2 className="h-4 w-4 animate-spin" /> Cargando…
        </div>
      ) : (
        <form onSubmit={handleSubmit} className="mt-6 max-w-2xl space-y-5">
          <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-5 shadow-sm">
            <h3 className="text-sm font-semibold text-[var(--text-secondary)]">1. Dime tu estado actual</h3>
            <div className="mt-4 grid grid-cols-2 gap-4">
              <label className="text-sm text-[var(--text-secondary)]">
                Peso (kg)
                <input
                  type="number"
                  min={1}
                  step="0.1"
                  value={weightKg}
                  onChange={(e) => setWeightKg(e.target.value)}
                  placeholder="100"
                  className="mt-1 w-full rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
                  required
                />
              </label>
              <label className="text-sm text-[var(--text-secondary)]">
                Estatura (cm)
                <input
                  type="number"
                  min={1}
                  step="0.1"
                  value={heightCm}
                  onChange={(e) => setHeightCm(e.target.value)}
                  placeholder="170"
                  className="mt-1 w-full rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
                  required
                />
              </label>
              <label className="col-span-2 text-sm text-[var(--text-secondary)]">
                Nivel de actividad / ejercicio
                <select
                  value={activityLevel}
                  onChange={(e) => setActivityLevel(e.target.value as ActivityLevel)}
                  className="mt-1 w-full rounded-lg border border-[var(--input-border)] bg-[var(--card-bg)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
                >
                  {Object.entries(ACTIVITY_LEVEL_LABEL).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>
            </div>
            {previewBmi !== null && (
              <p className="mt-3 text-xs text-[var(--text-muted)]">
                IMC estimado: <span className="font-semibold text-[var(--text-secondary)]">{previewBmi}</span>
              </p>
            )}
          </div>

          <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-5 shadow-sm">
            <h3 className="text-sm font-semibold text-[var(--text-secondary)]">2. ¿Qué metas tienes?</h3>
            <textarea
              value={goalsText}
              onChange={(e) => setGoalsText(e.target.value)}
              placeholder="Ej. Quiero bajar 10 kg en los próximos 3 meses sin perder músculo, y empezar a hacer ejercicio 3 veces por semana."
              rows={4}
              className="mt-3 w-full rounded-lg border border-[var(--input-border)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
              required
            />
          </div>

          {error && (
            <div className="flex items-center gap-1.5 text-sm text-red-600">
              <AlertCircle className="h-4 w-4 shrink-0" /> {error}
            </div>
          )}
          {saveMessage && <p className="text-sm text-green-600">{saveMessage}</p>}

          <div className="flex flex-wrap gap-3">
            <button
              type="submit"
              disabled={isGenerating}
              className="flex items-center gap-2 rounded-full bg-[var(--accent)] px-5 py-2.5 text-sm font-medium text-white disabled:opacity-50"
            >
              {isGenerating ? <Loader2 className="h-4 w-4 animate-spin" /> : <Target className="h-4 w-4" />}
              {isGenerating ? 'Generando plan…' : plan ? 'Generar nuevo plan' : 'Generar plan'}
            </button>
            <button
              type="button"
              onClick={handleSaveProfile}
              disabled={isSavingProfile}
              className="flex items-center gap-2 rounded-full border border-[var(--accent-border)] px-5 py-2.5 text-sm font-medium text-[var(--accent-text)] hover:bg-[var(--accent-soft)] disabled:opacity-50"
            >
              {isSavingProfile ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
              Guardar Objetivos y plan
            </button>
          </div>
        </form>
      )}

      {plan && <GoalPlanCard plan={plan} />}
    </div>
  );
}

function GoalPlanCard({ plan }: { plan: GoalPlan }) {
  return (
    <div className="mt-8 max-w-3xl space-y-5">
      <div className="rounded-2xl border border-[var(--accent-border)] bg-[var(--accent-soft)] p-5">
        <h3 className="text-base font-semibold text-[var(--accent-text)]">Tu plan</h3>
        <p className="mt-2 whitespace-pre-wrap text-sm text-[var(--text-secondary)]">{plan.summary}</p>
        <div className="mt-4 flex flex-wrap gap-4 text-sm">
          <Stat label="IMC" value={`${plan.bmi} (${plan.bmiCategory})`} />
          {plan.targetWeightKg != null && <Stat label="Peso objetivo" value={`${plan.targetWeightKg} kg`} />}
          {plan.estimatedWeeksToGoal != null && <Stat label="Tiempo estimado" value={`${plan.estimatedWeeksToGoal} semanas`} />}
          {plan.dailyCalorieTarget != null && <Stat label="Calorías/día" value={`${plan.dailyCalorieTarget} kcal`} />}
        </div>
      </div>

      {plan.macros && (plan.macros.proteinGrams != null || plan.macros.carbsGrams != null || plan.macros.fatGrams != null) && (
        <div className="grid grid-cols-3 gap-3">
          <MacroCard icon={<Beef className="h-4 w-4 text-rose-500" />} label="Proteína" value={plan.macros.proteinGrams} />
          <MacroCard icon={<Wheat className="h-4 w-4 text-amber-500" />} label="Carbos" value={plan.macros.carbsGrams} />
          <MacroCard icon={<Droplet className="h-4 w-4 text-blue-500" />} label="Grasa" value={plan.macros.fatGrams} />
        </div>
      )}

      <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-5 shadow-sm">
        <h4 className="flex items-center gap-2 text-sm font-semibold text-[var(--text-secondary)]">
          <Apple className="h-4 w-4 text-green-600" /> Plan de nutrición
        </h4>
        <p className="mt-2 text-sm text-[var(--text-secondary)]">{plan.nutritionPlan?.description}</p>
        {plan.nutritionPlan?.mealsPerDay != null && (
          <p className="mt-1 text-xs text-[var(--text-muted)]">Comidas por día sugeridas: {plan.nutritionPlan.mealsPerDay}</p>
        )}
        <BulletList items={plan.nutritionPlan?.keyRecommendations} />
      </div>

      <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-5 shadow-sm">
        <h4 className="flex items-center gap-2 text-sm font-semibold text-[var(--text-secondary)]">
          <Dumbbell className="h-4 w-4 text-orange-600" /> Plan de ejercicio
        </h4>
        <p className="mt-2 text-sm text-[var(--text-secondary)]">{plan.exercisePlan?.description}</p>
        {(plan.exercisePlan?.daysPerWeek != null || plan.exercisePlan?.minutesPerSession != null) && (
          <p className="mt-1 text-xs text-[var(--text-muted)]">
            {plan.exercisePlan?.daysPerWeek != null && `${plan.exercisePlan.daysPerWeek} días/semana`}
            {plan.exercisePlan?.daysPerWeek != null && plan.exercisePlan?.minutesPerSession != null && ' · '}
            {plan.exercisePlan?.minutesPerSession != null && `${plan.exercisePlan.minutesPerSession} min/sesión`}
          </p>
        )}
        <BulletList items={plan.exercisePlan?.keyRecommendations} />
      </div>

      {plan.milestones?.length > 0 && (
        <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-5 shadow-sm">
          <h4 className="flex items-center gap-2 text-sm font-semibold text-[var(--text-secondary)]">
            <Flag className="h-4 w-4 text-[var(--accent-text)]" /> Hitos
          </h4>
          <ul className="mt-3 space-y-2">
            {plan.milestones.map((m) => (
              <li key={m.weekNumber} className="flex gap-2 text-sm text-[var(--text-secondary)]">
                <span className="shrink-0 font-medium text-[var(--text-primary)]">Semana {m.weekNumber}:</span> {m.description}
              </li>
            ))}
          </ul>
        </div>
      )}

      {plan.tips?.length > 0 && (
        <div className="rounded-2xl border border-[var(--card-border)] bg-[var(--card-bg)] p-5 shadow-sm">
          <h4 className="flex items-center gap-2 text-sm font-semibold text-[var(--text-secondary)]">
            <Lightbulb className="h-4 w-4 text-yellow-500" /> Tips
          </h4>
          <BulletList items={plan.tips} />
        </div>
      )}
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-xs text-[var(--text-muted)]">{label}</p>
      <p className="font-semibold text-[var(--text-primary)]">{value}</p>
    </div>
  );
}

function MacroCard({ icon, label, value }: { icon: React.ReactNode; label: string; value: number | null }) {
  return (
    <div className="flex items-center gap-2 rounded-xl border border-[var(--card-border)] bg-[var(--card-bg)] p-3 shadow-sm">
      {icon}
      <div>
        <p className="text-xs text-[var(--text-muted)]">{label}</p>
        <p className="text-sm font-semibold text-[var(--text-primary)]">{value != null ? `${value} g` : '—'}</p>
      </div>
    </div>
  );
}

function BulletList({ items }: { items?: string[] }) {
  if (!items || items.length === 0) return null;
  return (
    <ul className="mt-3 space-y-1.5">
      {items.map((item, index) => (
        <li key={index} className="flex gap-2 text-sm text-[var(--text-secondary)]">
          <Flame className="mt-0.5 h-3.5 w-3.5 shrink-0 text-[var(--text-muted)]" />
          {item}
        </li>
      ))}
    </ul>
  );
}



