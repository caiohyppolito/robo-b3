export type ThemeChoice = "auto" | "light" | "dark";

const KEY = "robo-b3.theme";

export function loadTheme(): ThemeChoice {
  try {
    const value = localStorage.getItem(KEY);
    return value === "light" || value === "dark" ? value : "auto";
  } catch {
    return "auto";
  }
}

export function applyTheme(choice: ThemeChoice) {
  const root = document.documentElement;
  if (choice === "auto") delete root.dataset.theme;
  else root.dataset.theme = choice;
  try {
    localStorage.setItem(KEY, choice);
  } catch {
    // Sem armazenamento: o tema vale só nesta visita.
  }
}

export const nextTheme = (choice: ThemeChoice): ThemeChoice => (choice === "auto" ? "light" : choice === "light" ? "dark" : "auto");
