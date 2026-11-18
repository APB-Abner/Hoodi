# Lugares (autocomplete de destino e recomendação por risco registrado)

Autenticados (Bearer), limite 40/min por usuário. Corpos com campo desconhecido são recusados (400). Nada é guardado; as coordenadas e o texto não devem ir para log.

## POST /v1/lugares/sugerir
Corpo: `{ "texto": "Paulista" }` (3 a 80 caracteres, sem caractere de controle).
Resposta 200: `{ "situacao": "ok|indisponivel", "sugestoes": [{ "id", "rotulo", "contexto", "coordenada": { "latitude", "longitude" } }] }` (até 5).
Região fixa no servidor (retângulo de São Paulo, país BR). Falha do provedor → `situacao: "indisponivel"`.

## POST /v1/lugares/recomendar
Corpo: `{ "posicao": { "latitude", "longitude" }, "categoria": "farmacia|mercado|hospital|padaria|cafe|restaurante" }`.
A posição é arredondada a 3 casas antes de ir ao provedor. Raio 1500 m, até 8 lugares.
Resposta 200: `{ "situacao", "motivo", "versaoGrade", "faixa", "aviso", "lugares": [{ "id", "nome", "contexto", "coordenada", "distanciaMetros", "situacao": "com_dado|sem_dado|fora_cobertura", "nivel": 0-3|null }] }`.
Ordem: com dado (menor nível, depois distância), sem dado, fora da cobertura. `motivo`: `grade_indisponivel`, `fora_territorio`, `provedor_indisponivel`, `nenhum_lugar`.
Risco histórico (snapshot v3, faixa do horário atual), não previsão e não garantia de segurança.

## Pendências
Validar o esquema de resposta e o limite diário de geocodificação com a chave real do HeiGIT; filtro de log do corpo; retenção do proxy.

Red-team (Codex, 2026-11-18): LUG-01 (P1, hipótese) o texto digitado e as coordenadas arredondadas vão na URL ao HeiGIT; confirmar e documentar a retenção/log do provedor antes de produção e na ficha de privacidade. LUG-02 (P2) falta teto global de chamadas ao geocodificador. LUG-03 (P2) falta o incremento OpenAPI dos dois endpoints de lugares.
