import type { LucideIcon } from 'lucide-react';
import { MessageCircle, Utensils, Dumbbell, Scale, Target, Wallet, PiggyBank, GraduationCap, Heart, Package, BarChart3 } from 'lucide-react';

export interface DomainTab {
  path: string;
  label: string;
  icon: LucideIcon;
  end?: boolean;
}

export interface Domain {
  id: string;
  label: string;
  /** Domains with no real pages yet show as disabled "Próximamente" chips. */
  enabled: boolean;
  icon: LucideIcon;
  /** Tailwind color classes for the domain's icon badge and active-state accents. */
  accentText: string;
  accentBg: string;
  tabs: DomainTab[];
}

/** Add new domains/tabs here as the app grows beyond Salud (e.g. Finanzas, Educación). */
export const DOMAINS: Domain[] = [
  {
    id: 'salud',
    label: 'Salud',
    enabled: true,
    icon: Heart,
    accentText: 'text-sky-500',
    accentBg: 'bg-sky-500/15',
    tabs: [
      { path: '/', label: 'Agente', icon: MessageCircle, end: true },
      { path: '/nutricion', label: 'Nutrición', icon: Utensils },
      { path: '/dashboard', label: 'Dashboard', icon: BarChart3 },
      { path: '/productos', label: 'Productos', icon: Package },
      { path: '/catalogo-ejercicios', label: 'Catálogo Ejercicios', icon: Dumbbell },
      { path: '/ejercicio', label: 'Ejercicio', icon: Dumbbell },
      { path: '/peso', label: 'Peso', icon: Scale },
      { path: '/metas', label: 'Metas', icon: Target },
    ],
  },
  {
    id: 'finanzas',
    label: 'Finanzas',
    enabled: false,
    icon: Wallet,
    accentText: 'text-emerald-500',
    accentBg: 'bg-emerald-500/15',
    tabs: [
      { path: '/finanzas/gastos', label: 'Gastos', icon: Wallet },
      { path: '/finanzas/presupuesto', label: 'Presupuesto', icon: PiggyBank },
    ],
  },
  {
    id: 'educacion',
    label: 'Educación',
    enabled: false,
    icon: GraduationCap,
    accentText: 'text-indigo-400',
    accentBg: 'bg-indigo-400/15',
    tabs: [{ path: '/educacion', label: 'Agente', icon: GraduationCap, end: true }],
  },
];
