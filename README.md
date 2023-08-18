# BigLand
 
BigLand is a project where I'm exploring a variety of GPU accelerated effects. These are early prototypes and not production ready.

### Atmosphere
A realtime atmospheric scattering renderer based on the work of Sebastan Lague (https://www.youtube.com/watch?v=DxfEbulyFcY)

### Clouds
Realtime volumetric clouds integrated with the atmosphere renderer. These were written mostly from scratch but much inspiration was taken from the following resources:
https://www.youtube.com/watch?v=4QOcCGI6xOU
https://www.guerrilla-games.com/media/News/Files/The-Real-time-Volumetric-Cloudscapes-of-Horizon-Zero-Dawn.pdf
https://www.elopezr.com/temporal-aa-and-the-quest-for-the-holy-trail/
https://www.shadertoy.com/view/4dSBDt

### Grass
GPU instanced grass designed as a replacement for Unity's shitty terrain grass

### Skybox
A custom skybox shader that renders planetary rings

### Dynamic ambient light
A basic system for rendering reflection probes and ambient light probes at runtime when lighting changes