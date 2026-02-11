window.addEventListener('DOMContentLoaded', function () {

    const div = 10.0;
    const textures = ["grass.png", "waterbump.png", "floor.png", "ground.jpg"];

    var cameraPositionSet = false;
    var startedRendering = false;
    var modelId = 0;

    const layers = 4;
    var materials = new Array(layers);
    var rootNodes = new Array(layers);

    scene = null;

    /* Global Functions */

    removeMesh = function (name) {
        if (scene == null) return;

        scene.getMeshByName(name)?.dispose();
    }

    clear = function () {
        if (scene == null) return;

        for (i = scene.meshes.length - 1; i >= 0; i--) {
            const mesh = scene.meshes[i];
            if (mesh.name !== "skyBox")
                mesh.dispose();
        }
        cameraPositionSet = false;
    }

    setCamera = function (pos, look, height) {
        if (scene == null) return;

        if (cameraPositionSet) return;

        if (height === undefined) height = 0;

        const camera = scene.activeCamera;
        cameraPositionSet = true;
        camera.position = new BABYLON.Vector3(pos.x / div, (pos.z / div) + height, pos.y / div);
        camera.setTarget(new BABYLON.Vector3(look.x / div, look.z / div, look.y / div));
    }

    log = function (message) {
        if (scene == null) return;

        console.log(message);
        const element = document.getElementById('canvasText');

        if (element === null) {
            return;
        }

        element.innerHTML = message;
    }

    toggleWireFrame = function () {
        if (scene == null) return;

        for (let i = 0; i < materials.length; i++) {
            materials[i].wireframe = !materials[i].wireframe;
        }
    }

    toggleLayer = function (layer) {
        if (scene == null) return;

        // TriangleType 2^x => array index
        // Where TriangleType.None is excluded
        switch (layer) {
            case 1: layer = 0; break;
            case 2: layer = 1; break;
            case 4: layer = 2; break;
            case 8: layer = 3; break;
        }
        rootNodes[layer].setEnabled(!rootNodes[layer].isEnabled());
    }

    getColour = function (color) {

        switch (color) {
            case 1: return BABYLON.Color3.Red();
            case 2: return BABYLON.Color3.Green();
            case 3: return BABYLON.Color3.Blue();
            case 4: return BABYLON.Color3.Teal();
            case 5: return BABYLON.Color3.Teal();
            case 6: return new BABYLON.Color3(1, 0.6, 0);
            case 7: return BABYLON.Color3.Yellow();
            case 8: return BABYLON.Color3.Black();
            case 9: return BABYLON.Color3.Magenta();
            default: return BABYLON.Color3.White();
        }
    }

    getHeight = function (color) {
        switch (color) {
            case 2: return 5 / div;
            case 4: return 1 / div;
            case 7: return 1 / div;
            default: return 1 / div;
        }
    }

    /* SinalR MessagePack */

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/watchHub")
        .withHubProtocol(new signalR.protocols.msgpack.MessagePackHubProtocol())
        .withAutomaticReconnect([0, 3000, 5000, 10000, 15000, 30000])
        .build();

    connection.start().then(function () {
        console.log("Connected to SignalR Hub");
    }).catch(function (err) {
        return console.error(err.toString());
    });

    connection.on("removeMesh", removeMesh);
    connection.on("clear", clear);

    connection.on("drawLine", (array, height, color, name) => {
        if (scene == null) return;

        removeMesh(name);

        height /= 10;

        const v = new BABYLON.Vector3.FromArray(array);

        const points = [
            new BABYLON.Vector3(v.x / div, v.z / div, v.y / div),
            new BABYLON.Vector3(v.x / div, (v.z / div) + height, v.y / div)];

        const c = getColour(color);

        const line = BABYLON.MeshBuilder.CreateLines(name, { points: points }, scene);
        line.color = c;
        
        line.enableEdgesRendering();
        line.edgesWidth = 5.0;
        line.edgesColor = new BABYLON.Color4(c.r, c.g, c.b, 1);
    })

    connection.on("drawLines", (arrays, color, name) => {
        if (scene == null) return;

        if (arrays.length === 0)
            return;

        var height = 2.1 / div;
        if (name.includes("debug")) {
            height = getHeight(color);
        }

        removeMesh(name);

        const vectors = []
        for (let i = arrays.length - 1; i >= 0; i--) {
            const v = new BABYLON.Vector3.FromArray(arrays[i]);
            v.y = v.y + height;
            vectors.push(new BABYLON.Vector3(v.x / div, v.z / div, v.y / div));
        }

        const pcs = new BABYLON.PointsCloudSystem(name, div, scene);

        const c = getColour(color);

        pcs.addPoints(vectors.length, (particle, i) => {
            particle.position = vectors[i]
            particle.color = c;
        });

        pcs.buildMeshAsync();
    })

    connection.on("drawWeightedLines", (arrays, name) => {
        if (scene == null) return;

        if (arrays.length === 0)
            return;

        const height = getHeight(1); // green height

        removeMesh(name);

        // Find min/max W (cost) for normalization
        let minW = Infinity;
        let maxW = -Infinity;
        for (let i = 0; i < arrays.length; i++) {
            const w = arrays[i][3];
            if (w < minW) minW = w;
            if (w > maxW) maxW = w;
        }
        const range = maxW - minW;

        const pcs = new BABYLON.PointsCloudSystem(name, div, scene);

        pcs.addPoints(arrays.length, (particle, i) => {
            const v = arrays[i];
            particle.position = new BABYLON.Vector3(v[0] / div, (v[2] / div) + height, v[1] / div);

            // Normalize weight to [0..1] and map to HSL green(120)->yellow(60)->red(0)
            const t = range > 0 ? (v[3] - minW) / range : 0;
            const hue = (1 - t) * 120;
            particle.color = hslToColor4(hue);
        });

        pcs.buildMeshAsync();
    })

    // HSL to BABYLON.Color4 without string allocation.
    // Hardcoded s=1, l=0.5 (full saturation, mid lightness) since we only
    // vary hue along the green->red gradient.
    function hslToColor4(h) {
        const k1 = (0  + h / 30) % 12;
        const k2 = (8  + h / 30) % 12;
        const k3 = (4  + h / 30) % 12;
        const r = 0.5 - 0.5 * Math.max(Math.min(k1 - 3, 9 - k1, 1), -1);
        const g = 0.5 - 0.5 * Math.max(Math.min(k2 - 3, 9 - k2, 1), -1);
        const b = 0.5 - 0.5 * Math.max(Math.min(k3 - 3, 9 - k3, 1), -1);
        return new BABYLON.Color4(r, g, b, 1);
    }

    connection.on("drawPath", (arrays, color, name) => {
        if (scene == null) return;

        if (arrays.length === 0) return;

        const height = 0 //getHeight(color);

        const vectors = [];
        for (i = 0; i < arrays.length; i++) {
            const t = new BABYLON.Vector3.FromArray(arrays[i]);
            const v = new BABYLON.Vector3(t.x / div, (t.z / div) + height, t.y / div)
            vectors.push(v);
        }

        removeMesh(name);

        const c = getColour(color)

        const lines = BABYLON.MeshBuilder.CreateLines(name, { points: vectors }, scene);
        lines.enableEdgesRendering();
        lines.edgesWidth = 5.0;
        lines.edgesColor = new BABYLON.Color4(c.r, c.g, c.b, 1);
        lines.color = c;

        const start = new BABYLON.Vector3.FromArray(arrays[0]);
        const end = new BABYLON.Vector3.FromArray(arrays[arrays.length - 1]);
        setCamera(start, end, 20);

        //console.log("drawPath: " + name + " completed.");
    })

    connection.on("addModels", (loadedIndices, loadedPositions) => {
        if (scene == null) return;

        if (loadedPositions.length === 0)
            return;

        const start = new BABYLON.Vector3.FromArray(loadedPositions[0]);
        const end = new BABYLON.Vector3.FromArray(loadedPositions[loadedPositions.length - 1]);

        setCamera(start, end, 20);

        const positions = new Float32Array(loadedPositions.length * 3);
        const uvs = new Float32Array((loadedPositions.length) * 2);
        const baseX = -4, baseZ = -4, scale = 4;

        for (let i = 0; i < loadedPositions.length; i++) {
            const p = new BABYLON.Vector3.FromArray(loadedPositions[i]);
            const index = i * 3;
            positions[index] = p.x / div;
            positions[index + 1] = p.z / div;
            positions[index + 2] = p.y / div;

            const uvIndex = i * 2;
            uvs[uvIndex] = (positions[index] - baseX) / scale;
            uvs[uvIndex + 1] = (positions[index + 2] - baseZ) / scale;
        }

        for (let p = 0; p < loadedIndices.length; p++) {
            const indices = loadedIndices[p];

            if (indices.length === 0)
                continue;

            modelId++;
            const customMesh = new BABYLON.Mesh("custom" + modelId, scene);
            const normals = new Float32Array(positions.length);
            BABYLON.VertexData.ComputeNormals(positions, indices, normals);

            const vertexData = new BABYLON.VertexData();
            vertexData.positions = positions;
            vertexData.indices = indices;
            vertexData.normals = normals;
            vertexData.uvs = uvs;
            vertexData.applyToMesh(customMesh);

            customMesh.material = materials[p % materials.length];
            customMesh.parent = rootNodes[p % rootNodes.length];
        }
    });

    connection.on("drawBoundBox", (min, max, color, name) => {
        if (scene == null) return;

        removeMesh(name);

        const v1 = new BABYLON.Vector3.FromArray(min);
        const v2 = new BABYLON.Vector3.FromArray(max);

        const v11 = new BABYLON.Vector3(v1.x / div, v1.z / div, v1.y / div);
        const v22 = new BABYLON.Vector3(v2.x / div, v2.z / div, v2.y / div);

        const vertices = [
            v11.x, v11.y, v11.z,
            v22.x, v11.y, v11.z,
            v22.x, v22.y, v11.z,
            v11.x, v22.y, v11.z,
            v11.x, v11.y, v22.z,
            v22.x, v11.y, v22.z,
            v22.x, v22.y, v22.z,
            v11.x, v22.y, v22.z 
        ];

        const indices = [
            0, 1, 2, 0, 2, 3,
            1, 5, 6, 1, 6, 2,
            5, 4, 7, 5, 7, 6,
            4, 0, 3, 4, 3, 7,
            3, 2, 6, 3, 6, 7,
            4, 5, 1, 4, 1, 0 
        ];

        const material = new BABYLON.StandardMaterial(scene);
        material.diffuseColor = getColour(color);  
        material.wireframe = true;

        const box = new BABYLON.Mesh(name, scene);
        box.material = material;

        const data = new BABYLON.VertexData();
        data.positions = vertices;
        data.indices = indices;

        data.applyToMesh(box);
    });

    /* JSON Based */

    toggleSceneExplorer = function (enabled) {
        if (scene == null) return;

        if (enabled) {
            scene.debugLayer.show();
        }
        else {
            scene.debugLayer.hide();
        }
    }

    drawSphere = function (vector, color, name) {
        if (scene == null) return;

        vector = JSON.parse(vector);

        removeMesh(name);
        const sphere = BABYLON.Mesh.CreateSphere(name, 10.0, 0.5, scene, false, BABYLON.Mesh.DEFAULTSIDE);
        const material = new BABYLON.StandardMaterial(scene);
        material.alpha = 1;
        material.diffuseColor = getColour(color);
        sphere.material = material;
        sphere.position = new BABYLON.Vector3(vector.x / div, (vector.z / div) + getHeight(color), vector.y / div);

        //console.log("drawSphere: " + name + " completed.");
    }

    drawLine = function (vector, color, name) {
        if (scene == null) return;

        vector = JSON.parse(vector);

        //log("drawLine: " + name);

        var height = 2.1 / div;
        if (name.includes("debug")) {
            height = getHeight(color);
        }

        removeMesh(name);

        const points = [
            new BABYLON.Vector3(vector.x / div, vector.z / div, vector.y / div),
            new BABYLON.Vector3(vector.x / div, (vector.z / div) + height, vector.y / div)];

        const c = getColour(color);

        const line = BABYLON.MeshBuilder.CreateLines(name, { points: points }, scene);
        line.color = c;
   
        line.enableEdgesRendering();
        line.edgesWidth = 5.0;
        line.edgesColor = new BABYLON.Color4(c.r, c.g, c.b, 1);
        
        if (name === "start") {
            setCamera(vector, vector, 10);
        }
    }

    drawLineDebug = function (vector, color, name) {
        if (scene == null) return;

        drawLine(vector, color, name);
    }

    drawLineDebugs = function (vectors, color, name) {
        if (scene == null) return;

        vectors = JSON.parse(vectors);
        for (let i = vectors.length - 1; i >= 0; i--) {
            const v = vectors[i];
            drawLineDebug(JSON.stringify(v), color, name + i);
        }
    }

    drawPath = function (points, color, name) {
        if (scene == null) return;

        points = JSON.parse(points);

        //log("drawPath: " + name);

        const path = [];
        for (i = 0; i < points.length; i++) {
            const p = points[i];
            const height = getHeight(color);
            path.push(new BABYLON.Vector3(p.x / div, (p.z / div) + height, p.y / div));
        }

        removeMesh(name);

        const lines = BABYLON.MeshBuilder.CreateLines(name, { points: path }, scene);
        lines.color = getColour(color);

        setCamera(points[0], points[points.length - 1], 20);

        //console.log("drawPath: " + name + " completed.");
    }

    createScene = function () {
        log("createScene: started");

        canvas = document.getElementById('renderCanvas');// get the canvas DOM element
        engine = new BABYLON.Engine(canvas, true); // load the 3D engine
        engine.setHardwareScalingLevel(0.5);

        scene = new BABYLON.Scene(engine);// create a basic BJS Scene object

        var light = new BABYLON.HemisphericLight("hemi", new BABYLON.Vector3(1, 1, 0), scene);
        light.intesity = 0.5;

        // the canvas/window resize event handler
        window.addEventListener('resize', function () { engine.resize(); });

        camera = new BABYLON.FreeCamera('camera1', new BABYLON.Vector3(0, 50, 0), scene);
        camera.keysUp.push(87);         // "w"
        camera.keysDown.push(83);       // "s"
        camera.keysLeft.push(65);       // "a"
        camera.keysRight.push(68);      // "d"
        camera.keysDownward.push(81);   // "q"
        camera.keysUpward.push(69);     // "e"
        camera.attachControl(canvas, false); // attach the camera to the canvas

        const cameraSlowSpeed = 0.01;
        const cameraMinSpeed = 0.1;
        const cameraMaxSpeed = 1;
        camera.speed = cameraMinSpeed;

        // create layers
        for (let i = 0; i < layers; i++) {
            rootNodes[i] = new BABYLON.TransformNode();

            const mat = new BABYLON.StandardMaterial("mat" + i, scene);
            mat.diffuseTexture = new BABYLON.Texture("https://www.babylonjs-playground.com/textures/" + textures[i])
            mat.backFaceCulling = false;
            materials[i] = mat;
        }

        // Skybox
        const skybox = BABYLON.MeshBuilder.CreateBox("skyBox", { size: 4000.0 }, scene);
        const skyboxMaterial = new BABYLON.StandardMaterial("skyBox", scene);
        skyboxMaterial.backFaceCulling = false;
        skyboxMaterial.reflectionTexture = new BABYLON.CubeTexture("https://www.babylonjs-playground.com/textures/skybox", scene);
        skyboxMaterial.reflectionTexture.coordinatesMode = BABYLON.Texture.SKYBOX_MODE;
        skyboxMaterial.diffuseColor = new BABYLON.Color3(0, 0, 0);
        skyboxMaterial.specularColor = new BABYLON.Color3(0, 0, 0);
        skybox.material = skyboxMaterial;

        engine.runRenderLoop(function () {
            if (!scene.paused) {
                scene.render();
            }
        });

        var energy = 0;
        var shiftPressed = false;
        var altPressed = false;

        scene.onBeforeRenderObservable.add(function () {
            if (shiftPressed) {
                camera.speed = cameraMaxSpeed;
                energy = 25;
            }
            else if (altPressed) {
                camera.speed = cameraSlowSpeed;
                energy = 25;
            } else {
                if (energy > 0) {
                    energy--;
                }
                else {
                    camera.speed = cameraMinSpeed;
                }
            }
        });
        scene.onKeyboardObservable.add((kbInfo) => {
            switch (kbInfo.type) {
                case BABYLON.KeyboardEventTypes.KEYDOWN:
                    switch (kbInfo.event.key) {
                        case "Shift":
                            shiftPressed = true;
                            kbInfo.event.preventDefault();
                            break;
                        case "Alt":
                            altPressed = true;
                            kbInfo.event.preventDefault();
                            break;
                    }
                    break;

                case BABYLON.KeyboardEventTypes.KEYUP:
                    switch (kbInfo.event.key) {
                        case "Shift":
                            shiftPressed = false;
                            kbInfo.event.preventDefault();
                            break;
                        case "Alt":
                            altPressed = false;
                            kbInfo.event.preventDefault();
                            break;
                        case "o":
                            log("Camera Position: " + camera.position);
                            break;
                    }
            }
        });

        log("createScene: completed");
    };
});