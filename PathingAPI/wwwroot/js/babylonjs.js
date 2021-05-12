window.addEventListener('DOMContentLoaded', function () {

    showAlert = (message) => {
        alert(message);
    }

    log = function (message) {
        console.log(log);
        document.getElementById('canvasText').innerHTML = message;
    }

    removeMeshes = function (name) {
        for (i = scene.meshes.length - 1; i >= 0; i--) {
            var mesh = scene.meshes[i];
            if (mesh.name === name) { mesh.dispose(); }
        }
    }

    clear = function () {
        for (i = scene.meshes.length - 1; i >= 0; i--) {
            var mesh = scene.meshes[i];
            if (mesh.name !== "skyBox") {
                console.log("deleting mesh:" + mesh.name);
                mesh.dispose();
            }
        }
        cameraPositionSet = false;
    }

    drawSphere = function (vector, col, name) {

        removeMeshes(name);
        var sphere = BABYLON.Mesh.CreateSphere(name, 10.0, 0.5, scene, false, BABYLON.Mesh.DEFAULTSIDE);
        var material = new BABYLON.StandardMaterial(scene);
        material.alpha = 1;
        material.diffuseColor = getColour(col);
        sphere.material = material;
        sphere.position = new BABYLON.Vector3(vector.x, vector.z+0.2, vector.y);

        console.log("drawSphere: " + name + " completed.");
    }

    drawLine = function (vector, col, name) {
        log("drawLine: " + name);
        removeMeshes(name);
        var line1 = [new BABYLON.Vector3(vector.x, vector.z, vector.y), new BABYLON.Vector3(vector.x, vector.z+10, vector.y)];
        var lines1 = BABYLON.MeshBuilder.CreateLines(name, { points: line1 }, scene);
        lines1.color = getColour(col);

        if (!cameraPositionSet || name === "start") {
            cameraPositionSet = true;
            camera.setTarget(new BABYLON.Vector3(vector.x, vector.z, vector.y));
            camera.position = new BABYLON.Vector3(vector.x, vector.z + 10, vector.y);
        }

        console.log("drawLine: " + name + " completed.");
    }

    cameraPositionSet = false;
    startedRendering = false;
    modelId = 0;

    function getColour(col)
    {
        if (col === 1) { return BABYLON.Color3.Red(); }
        if (col === 2) { return BABYLON.Color3.Green(); }
        if (col === 3) { return BABYLON.Color3.Blue(); }
        if (col === 4) { return BABYLON.Color3.White(); }
        if (col === 5) { return BABYLON.Color3.Teal(); }
        if (col === 6) { return new BABYLON.Color3(1, 0.6, 0); } // orange
        return BABYLON.Color3.White();
    }

    drawPath = function (pathPoints, col, name) {
        log("drawPath: " + name);
        var path = [];

        for (i = 0; i < pathPoints.length; i++) {
            var element = pathPoints[i];
            var height = col === 4 ? 0.1 : 0.11;
            path.push(new BABYLON.Vector3(element.x, element.z + height, element.y));
        }

        var lines = BABYLON.MeshBuilder.CreateLines(name, { points: path }, scene);
        lines.color = getColour(col);

        var camera = scene.activeCamera;
        var startPoint = 0;
        var endPoint = pathPoints.length - 1;

        if (!cameraPositionSet) {
            cameraPositionSet = true;
            camera.setTarget(new BABYLON.Vector3(pathPoints[endPoint].x, pathPoints[endPoint].z, pathPoints[endPoint].y));
            camera.position = new BABYLON.Vector3(pathPoints[startPoint].x, pathPoints[startPoint].z + 20, pathPoints[startPoint].y);
        }

        console.log("drawPath: " + name + " completed.");
    }

    createScene = function () {
        log("createScene");
        canvas = document.getElementById('renderCanvas');// get the canvas DOM element
        engine = new BABYLON.Engine(canvas, true); // load the 3D engine

        scene = new BABYLON.Scene(engine);// create a basic BJS Scene object

        var light = new BABYLON.HemisphericLight("hemi", new BABYLON.Vector3(1, 1, 0), scene);
        light.intesity = 0.5;

        // the canvas/window resize event handler
        window.addEventListener('resize', function () { engine.resize(); });

        camera = new BABYLON.FreeCamera('camera1', new BABYLON.Vector3(0, 50, -0), scene);
        camera.attachControl(canvas, false); // attach the camera to the canvas

        var cameraMinSpeed = 0.1;
        var cameraMaxSpeed = 1;
        camera.speed = cameraMinSpeed;

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
            if (!startedRendering) {
                scene.render();
            }
        });

        var energy = 1000, shiftPressed = false;
        scene.onBeforeRenderObservable.add(function () {
            if (shiftPressed) {
                if (energy > 0) {
                    camera.speed = cameraMaxSpeed;
                    energy--;
                }
                else {
                    camera.speed = cameraMinSpeed;
                }
            } else {
                camera.speed = cameraMinSpeed;
            }
        });
        scene.onKeyboardObservable.add((kbInfo) => {
            switch (kbInfo.type) {
                case BABYLON.KeyboardEventTypes.KEYDOWN:
                    switch (kbInfo.event.key) {
                        case "Shift":
                            shiftPressed = true;
                            break;
                    }
                    break;

                case BABYLON.KeyboardEventTypes.KEYUP:
                    switch (kbInfo.event.key) {
                        case "Shift":
                            shiftPressed = false;
                            break;
                        case "o":
                            log("Camera Position: " + camera.position);
                            break;
                    }
            }
        });

        console.log("createScene: completed.");
    };

    addModels = function (loadedIndices, loadedPositions) {
        log("addModels: " + modelId);
        var positions = [];

        if (loadedPositions.length === 0) {
            return;
        }

        if (!cameraPositionSet) {
            camera.setTarget(new BABYLON.Vector3(loadedPositions[0].x, loadedPositions[0].z, loadedPositions[0].y));
            camera.position = new BABYLON.Vector3(loadedPositions[loadedPositions.length - 1].x, loadedPositions[loadedPositions.length - 1].z + 10, loadedPositions[loadedPositions.length-1].y);
        }

        for (i = 0; i < loadedPositions.length; i++) {
            positions.push(loadedPositions[i].x);
            positions.push(loadedPositions[i].z);
            positions.push(loadedPositions[i].y);
        }

        //take uv value relative to bottom left corner of roof (-4, -4) noting length and width of roof is 8. base uv value on the x, z coordinates only
        var uvs = [];
        for (var p = 0; p < positions.length / 3; p++) {
            uvs.push((positions[3 * p] - (-4)) / 4, (positions[3 * p + 2] - (-4)) / 4);
        }

        var textures = ["grass.png", "waterbump.png", "floor.png", "ground.jpg"];

        // add the models
        for (var p = 0; p < loadedIndices.length; p++) {
            var indices = loadedIndices[p];

            modelId++;
            var customMesh = new BABYLON.Mesh("custom" + modelId, scene);
            var normals = [];
            BABYLON.VertexData.ComputeNormals(positions, indices, normals);
            var vertexData = new BABYLON.VertexData();
            vertexData.positions = positions;
            vertexData.indices = indices;
            vertexData.normals = normals;
            vertexData.uvs = uvs;
            vertexData.applyToMesh(customMesh);
            //customMesh.convertToFlatShadedMesh();

            var mat1 = new BABYLON.StandardMaterial("mat" + modelId, scene);

            mat1.diffuseTexture = new BABYLON.Texture("https://www.babylonjs-playground.com/textures/" + textures[p])
            //mat.wireframe = true;
            mat1.backFaceCulling = false;
            customMesh.material = mat1;
        }

        if (!startedRendering) {
            startedRendering = true;
            // run the render loop
            engine.runRenderLoop(function () {
                scene.render();
            });
        }

        console.log("addModels completed");
    }
});