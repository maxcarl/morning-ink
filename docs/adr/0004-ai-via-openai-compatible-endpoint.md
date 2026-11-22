# 4. AI via an OpenAI-compatible endpoint

- Status: accepted
- Date: 2026-11-22

## Context

Morning Ink works without AI; AI adds summaries and later more. Users run very different providers: Ollama or LM Studio at home, often on a separate server in the home network, or a cloud service. The home network is blocked for all outgoing requests (see the security rules), and a model on a CPU can take minutes per answer.

## Decision

- One provider type: an OpenAI-compatible chat endpoint, used through Microsoft.Extensions.AI and its OpenAI adapter (MIT). It covers Ollama, LM Studio, vLLM, OpenRouter and OpenAI. Anthropic can follow as its own provider.
- Address, model and key are instance settings in the database, edited by the admin; the key is encrypted like the mail password and never shown again.
- Requests use their own HTTP client: every connection passes the same home-network check, but without the response size limit and with a long network timeout. Marking the provider as local ("runs on my own hardware") allows its host in the home network when the admin saves it: naming the endpoint is that decision.
- AI is a processing step after the full text. It never costs an article: settings missing, a source marked "local AI only" with a cloud provider, or any failure leave the article as it is; after the first failure, the rest of the issue run does not ask again.
- Article text goes to the model fenced in as data, after fixed instructions from a prompt file versioned in the repository. Answers are checked: markup removed, empty or overlong answers dropped, and they reach the issue only as text.

## Consequences

- No AI code per provider; switching from Ollama to a cloud service is a settings change.
- The admin decides which host in the home network the AI may reach, without a separate allowlist step.
- Summaries take as long as the model needs; a slow CPU model needs an earlier start (task 38).
