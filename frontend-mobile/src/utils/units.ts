const KG_PER_LB = 0.45359237;
const CM_PER_INCH = 2.54;

export function kgToLb(kg: number): number {
  return kg / KG_PER_LB;
}

export function lbToKg(lb: number): number {
  return lb * KG_PER_LB;
}

/** Converts kg to lb and rounds to 1 decimal, for display. */
export function formatLb(kg: number): string {
  return (Math.round(kgToLb(kg) * 10) / 10).toString();
}

export function cmToFeetInches(cm: number): { feet: number; inches: number } {
  const totalInches = cm / CM_PER_INCH;
  const feet = Math.floor(totalInches / 12);
  const inches = Math.round(totalInches - feet * 12);
  return feet >= 0 && inches === 12 ? { feet: feet + 1, inches: 0 } : { feet, inches };
}

export function feetInchesToCm(feet: number, inches: number): number {
  return (feet * 12 + inches) * CM_PER_INCH;
}
