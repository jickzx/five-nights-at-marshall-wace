// The employers band on the QS page reads only this list.
//
// PLACEHOLDERS. Every name below is invented for the demo and is not a customer. Do not put a real
// company's name or logo here unless that company has agreed to be shown: listing a firm as a customer
// when it is not is a false claim on a public page, and its logo is its trademark.
//
// To show your real employers, replace this array (one edit). `mark` is the seed for the procedural
// glyph drawn as the logo; supply `logo` (a path under public/) instead to use a real, licensed logo.

export interface Employer {
  name: string;
  sector: string;
  mark: number;
  logo?: string;
}

export const EMPLOYERS_ARE_PLACEHOLDERS = true;

export const EMPLOYERS: Employer[] = [
  { name: "Quillmere Analytics", sector: "Data platforms", mark: 11 },
  { name: "Fennick & Loam", sector: "Risk advisory", mark: 27 },
  { name: "Okapi Lattice", sector: "Systems research", mark: 43 },
  { name: "Brindlecourt Labs", sector: "Applied linguistics", mark: 58 },
  { name: "Marrowgate Partners", sector: "Public-sector analytics", mark: 71 },
  { name: "Sable Quarry Research", sector: "Quant research", mark: 86 },
];
