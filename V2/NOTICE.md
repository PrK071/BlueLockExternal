# BlueLockExternal V2 — componentes externos

Este MVP usa OpenCvSharp e OpenCV (Apache-2.0), além de System.Memory, System.Buffers, System.Numerics.Vectors e System.Runtime.CompilerServices.Unsafe, da Microsoft (MIT). As licenças estão na pasta `licenses`.

O replay de vídeo usa bibliotecas do projeto FFmpeg sob LGPLv2.1 ou posterior, através de `opencv_videoio_ffmpeg4130_64.dll`. A documentação de origem está em `licenses/FFmpeg-OpenCV-readme.txt`; os termos estão em `licenses/FFmpeg-LGPL-2.1.txt`.

Origens:

- https://github.com/shimat/opencvsharp/tree/b161e7e012f5101f6d5dc68a835c59db6cc88b18
- https://github.com/opencv/opencv/tree/4.13.0
- https://github.com/opencv/opencv_3rdparty/tree/d82ad9a54a7b42a1648a9cae8fed5c2f20ea396c
- https://github.com/dotnet/maintenance-packages/tree/f62ca0009b038cab4725a720f386623a969d73ad
- https://ffmpeg.org/legal.html

O MD5 do plugin FFmpeg coincide com o registrado pelo OpenCV 4.13.0: `3248b4663ffef770cdb54ec8b9d16a28`. O SHA-256 dos pacotes está fixado em `dependencies.lock.json`. MD5 aqui serve apenas para correlacionar o arquivo com os metadados do fornecedor.

Distribuição comercial ainda exige reunir os fontes correspondentes e instruções de construção do FFmpeg e todos os avisos dos componentes nativos incorporados. O inventário deste MVP não encerra essa auditoria. O runtime completo informa `Non-free algorithms: YES`; o aplicativo não utiliza esses algoritmos. Para uma distribuição comercial com inventário menor, a próxima construção nativa deverá selecionar apenas os módulos usados e desabilitar `OPENCV_ENABLE_NONFREE`.

Nenhuma licença dos componentes externos transfere propriedade sobre o código próprio do aplicativo. A licença comercial desse código não foi definida neste trabalho.
