import { render, screen } from '@testing-library/react'
import App from './App'

// Smoke test — confirms the toolchain (Vitest + RTL + jsdom) is wired up.
// Real tests start arriving at Lesson 1.1.
describe('App', () => {
  it('renders the app heading', () => {
    render(<App />)
    expect(screen.getByRole('heading', { name: /wayfinders/i })).toBeInTheDocument()
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(4)
  })
})
