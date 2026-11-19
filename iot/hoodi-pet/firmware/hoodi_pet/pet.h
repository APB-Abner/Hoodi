// Etapas do hoodi Pet chamadas pelo setup() e pelo loop() de hoodi_pet.ino.
#pragma once

namespace pet {

void iniciar();            // pinos, tela, nome Bluetooth e modo pareamento

void receberComandos();    // comandos do app que chegaram pelo Bluetooth
void atualizarBle();       // conectou, desconectou, PIN na tela, pareado
void atualizarSerial();    // comandos de bancada (n0..n3, id, fim, st...)
void atualizarBotoes();    // botao 1 tela, botao 2 silenciar, botao 3 ajuda (pressao longa)
void atualizarConexao();   // vibra 1x se o celular sumir durante um trajeto
void atualizarVibracao();  // liga/desliga o motor conforme o padrao em andamento
void atualizarSom();       // toca a nota do toque em andamento no alto-falante (0 = silencio)
void atualizarTela();      // apaga a tela depois de 10 s sem novidade

}  // namespace pet
