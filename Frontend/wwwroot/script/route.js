var DotNet = null;
var panzoom = null;
var gridNode = null;
var prect = null;

function init(obj) {
    DotNet = obj;
}
function dispose() {
    DotNet = null;
}

function draw(color3, color4) {

    gridNode = document.getElementById("grid")
    panzoom = Panzoom(gridNode, { /*contain: 'outside',*/ excludeClass: 'group2', minScale: 0.99, maxScale: 8, roundPixels: true })
    const parent = gridNode.parentElement
    parent.addEventListener('wheel', panzoom.zoomWithWheel)

    function gridData() {
        var data = new Array();
        var xpos = 1;
        var ypos = 1;
        var width = 20;
        var height = 20;
        var size = 30;
        // total size => 600x600 px

        for (var row = 0; row < size; row++) {
            data.push(new Array());

            for (var column = 0; column < size; column++) {
                data[row].push({ x: xpos, y: ypos, width: width, height: height })
                xpos += width;
            }
            xpos = 1;
            ypos += height;
        }
        return data;
    }

    var gridData = gridData();

    var grid = d3.select("#grid").select("svg").select(".background");

    var row = grid.selectAll(".row")
        .data(gridData)
        .enter()
        .append("g")
        .attr("class", "row");

    var column = row.selectAll(".square")
        .data(function (d) { return d; })
        .enter().append("rect")
        .attr("class", "square")
        .attr("x", function (d) { return d.x; })
        .attr("y", function (d) { return d.y; })
        .attr("width", function (d) { return d.width; })
        .attr("height", function (d) { return d.height; })
        .style("fill", color3)
        .style("stroke-width", "0.5px")
        .style("stroke", color4);
}

function showTooltip(evt, text) {
    let tooltip = document.getElementById("tooltip");
    tooltip.innerHTML = text;
    tooltip.style.display = "block";

    if (evt.clientX) {
        tooltip.style.left = evt.clientX + 10 + 'px';
        tooltip.style.top = evt.clientY + 10 + 'px';
    }
    else {
        tooltip.style.left = evt.pageX + 10 + 'px';
        tooltip.style.top = evt.pageY + 10 + 'px';
    }
}

function hideTooltip() {
    var tooltip = document.getElementById("tooltip");
    tooltip.style.display = "none";
}

function pointClick(evt, x, y, i) {
    if (DotNet == null) return;

    DotNet.invokeMethodAsync('PointClick', x, y, i);
}

function focusAt(payload) {
    if (panzoom == null) return;

    //console.log(payload);
    //var rect = gridNode.getBoundingClientRect();
    var p = document.getElementById("playerloc");
    if (p != null) {
        prect = p.getBoundingClientRect();
        //let zoom = 1.5;
        let zoom = panzoom.getScale();
        panzoom.zoomToPoint(zoom, { clientX: prect.left, clientY: prect.top })
    }
    ///var point = JSON.parse(payload);
    //panzoom.zoomToPoint(panzoom.getScale(), { clientX: rect.left + payload.X, clientY: rect.top + payload.Y });
}