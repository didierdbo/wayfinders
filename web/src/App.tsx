// Wayfinders — web companion app.

import { CharacterCard } from '@/components/CharacterCard'
import { characters } from '@/fixtures/fixtureCharacters'

// Kept intentionally empty until Lesson 1.1 (Character Card Viewer).
export default function App() {
  return (
    <>
      <h1>Wayfinders</h1>
      <p>Web companion — scaffold ready. First lesson: Character Card Viewer.</p>

      {characters.map((c) => (
        <CharacterCard character={c} />
      ))}
    </>
  )
}
