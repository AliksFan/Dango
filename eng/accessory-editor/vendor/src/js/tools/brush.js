import alertify from 'dango-notify';
import app from './../app.js';
import config from './../config.js';
import Base_tools_class from './../core/base-tools.js';
import Base_layers_class from './../core/base-layers.js';

class Brush_class extends Base_tools_class {

	constructor(ctx) {
		super();
		this.Base_layers = new Base_layers_class();
		this.name = 'brush';
		this.layer = {};
		this.params_hash = false;
		this.pressure_supported = false;
		this.pointer_pressure = 0; // has range [0 - 1]
		this.max_speed = 20;
		this.power = 2; //how speed affects size
		this.event_links = [];
		this.data_index = 0;
	}

	load() {
		var _this = this;
		var is_touch = false;

		//pointer events
		document.addEventListener('pointerdown', function (event) {
			_this.pointerdown(event);
		});
		document.addEventListener('pointermove', function (event) {
			_this.pointermove(event);
		});

		//mouse events
		document.addEventListener('mousedown', function (event) {
			if(is_touch)
				return;
			_this.dragStart(event);
		});
		document.addEventListener('mousemove', function (event) {
			if(is_touch)
				return;
			_this.dragMove(event);
		});
		document.addEventListener('mouseup', function (event) {
			if(is_touch)
				return;
			_this.dragEnd(event);
		});

		// collect touch events
		document.addEventListener('touchstart', function (event) {
			is_touch = true;
			_this.dragStart(event);
		});
		document.addEventListener('touchmove', function (event) {
			_this.dragMove(event);
		});
		document.addEventListener('touchend', function (event) {
			_this.dragEnd(event);
		});
	}

	pointerdown(e) {
		// Devices that don't actually support pen pressure can give 0.5 as a false reading.
		// It is highly unlikely a real pen will read exactly 0.5 at the start of a stroke.
		if (e.pressure && e.pressure !== 0 && e.pressure !== 0.5 && e.pressure <= 1) {
			this.pressure_supported = true;
			this.pointer_pressure = e.pressure;
		} else {
			this.pressure_supported = false;
		}
	}

	pointermove(e) {
		// Pressure of exactly 1 seems to be an input error, sometimes I see it when lifting the pen
		// off the screen when pressure reading should be near 0.
		if (this.pressure_supported && e.pressure < 1) {
			this.pointer_pressure = e.pressure;
		}
	}

	dragStart(event) {
		var _this = this;
		if (config.TOOL.name != _this.name)
			return;
		this.click_counter++;

		var mouse = this.get_mouse_info(event);
		if (mouse.is_drag == false)
			return;
		if (mouse.click_valid == false) {
			return;
		}

		var events = [];
		if (event.changedTouches) {
			events = event.changedTouches;
		}
		else{
			events.push(event);
		}
		for(var i = 0; i < events.length; i++){
			var identifier = null;
			if(typeof events[i].identifier != "undefined") {
				identifier = events[i].identifier;
			}

			this.event_links.push({
				identifier: identifier,
				index: this.data_index,
			});

			_this.mousedown_action(events[i], this.data_index, identifier);

			this.data_index++;
		}
	}

	dragMove(event) {
		var _this = this;
		if (config.TOOL.name != _this.name)
			return;

		if (typeof event.changedTouches == "undefined") {
			//mouse cursor
			var mouse = _this.get_mouse_info(event);
			var params = _this.getParams();
			_this.show_mouse_cursor(mouse.x, mouse.y, params.size, 'circle');
		}

		var mouse = this.get_mouse_info(event);
		if (mouse.is_drag == false)
			return;
		if (mouse.click_valid == false) {
			return;
		}

		var events = [];
		if (event.changedTouches) {
			events = event.changedTouches;
		}
		else{
			events.push(event);
		}
		for(var i = 0; i < events.length; i++){
			var identifier = null;
			if(typeof events[i].identifier != "undefined") {
				identifier = events[i].identifier;
			}

			for(var j = 0; i < this.event_links.length; j++){
				if(this.event_links[j].identifier == identifier){
					//found link
					_this.mousemove_action(events[i], this.event_links[j].index);
					break;
				}
			}
		}
	}

	dragEnd(event) {
		var _this = this;
		if (config.TOOL.name != _this.name)
			return;

		var mouse = this.get_mouse_info(event);
		if (mouse.click_valid == false) {
			return;
		}

		var events = [];
		if (event.changedTouches) {
			events = event.changedTouches;
		}
		else{
			events.push(event);
		}
		for(var i = 0; i < events.length; i++){
			var identifier = null;
			if(typeof events[i].identifier != "undefined") {
				//unlink
				identifier = events[i].identifier;
			}

			for(var j = 0; i < this.event_links.length; j++){
				if(this.event_links[j].identifier == identifier){
					this.event_links.splice(j, 1);
					break;
				}
			}

			_this.mouseup_action(events[i]);
		}
	}

	async mousedown_action(e, index, event_identifier) {
      this.blockedStroke=false;this.strokePending=false;
      const requiresNew=config.layer.type!==this.name || this.get_params_hash()!==this.params_hash;
      if(config.layers.length>=16 && requiresNew) {this.blockedStroke=true;alertify.error('图层资源不足，已保留当前结果。');return;}
      if(!app.State.can_add_payload(1048576)) {this.blockedStroke=true;alertify.error('撤销资源不足，已保留当前结果。');return;}
    
		var mouse = this.get_mouse_info(e);
		if (mouse.click_valid == false)
			return;

		var params_hash = this.get_params_hash();

		if (config.layer.type != this.name || params_hash != this.params_hash) {
			//register new object - current layer is not ours or params changed
			this.layer = {
				type: this.name,
				data: [[]],
				params: this.clone(this.getParams()),
				status: 'draft',
				render_function: [this.name, 'render'],
				x: 0,
				y: 0,
				width: config.WIDTH,
				height: config.HEIGHT,
				hide_selection_if_active: true,
				rotate: null,
				is_vector: true,
				color: config.COLOR
			};
			this.strokePending=true;
			const outcome=await app.State.do_action(
				new app.Actions.Bundle_action('new_brush_layer', 'New Brush Layer', [
					new app.Actions.Insert_layer_action(this.layer)
				])
			);
			this.strokePending=false;if(outcome?.status!=='completed'){this.blockedStroke=true;return;}
			this.params_hash = params_hash;

			//reset event links index
			this.data_index = 0;
			index = 0;
			this.event_links = [];
			this.event_links.push({
				identifier: event_identifier,
				index: this.data_index,
			});
		}
		else {
			const new_data = JSON.parse(JSON.stringify(config.layer.data));
			new_data.push([]);
			this.strokePending=true;
			const outcome=await app.State.do_action(
				new app.Actions.Bundle_action('update_brush_layer', 'Update Brush Layer', [
					new app.Actions.Update_layer_action(config.layer.id, {
						data: new_data
					})
				])
			);
			this.strokePending=false;if(outcome?.status!=='completed'){this.blockedStroke=true;return;}
		}

		//in case of undo, recalculate index
		for(var i = index; i >= 0; i++){
			if(typeof config.layer.data[index] != "undefined"){
				break;
			}
			index--;
		}

		var current_group = config.layer.data[index];
		var params = this.getParams();

		//detect line size
		var size = params.size;
		var new_size = size;

		if (params.pressure == true) {
			if (this.pressure_supported) {
				new_size = size * this.pointer_pressure * 2;
			}
			else {
				new_size = size + size / this.max_speed * mouse.speed_average * this.power;
				new_size = Math.max(new_size, size / 4);
				new_size = Math.round(new_size);
			}
		}

		var mouse_coords = this.get_mouse_coordinates_from_event(e);
		var mouse_x = mouse_coords.x;
		var mouse_y = mouse_coords.y;

		if(config.layer.data.reduce((n,g)=>n+g.length,0)>=32768 || !app.State.can_add_payload(512)) {alertify.error('Stroke or undo budget exceeded.');return;}
      current_group.push([mouse_x - config.layer.x, mouse_y - config.layer.y, new_size]);
		this.Base_layers.render();
	}

	mousemove_action(e, index) { if(this.blockedStroke||this.strokePending||config.layer.type!=='brush')return;
		var mouse = this.get_mouse_info(e);
		if (mouse.is_drag == false)
			return;
		if (mouse.click_valid == false) {
			return;
		}

		//in case of undo, recalculate index
		for(var i = index; i >= 0; i++){
			if(typeof config.layer.data[index] != "undefined"){
				break;
			}
			index--;
		}

		var params = this.getParams();
		var current_group = config.layer.data[index];

		//detect line size
		var size = params.size;
		var new_size = size;

		if (params.pressure == true) {
			if (this.pressure_supported) {
				new_size = size * this.pointer_pressure * 2;
			}
			else {
				new_size = size + size / this.max_speed * mouse.speed_average * this.power;
				new_size = Math.max(new_size, size / 4);
				new_size = Math.round(new_size);
			}
		}

		var mouse_coords = this.get_mouse_coordinates_from_event(e);
		var mouse_x = mouse_coords.x;
		var mouse_y = mouse_coords.y;

		if(config.layer.data.reduce((n,g)=>n+g.length,0)>=32768 || !app.State.can_add_payload(512)) {alertify.error('Stroke or undo budget exceeded.');return;}
      current_group.push([mouse_x - config.layer.x, mouse_y - config.layer.y, new_size]);
		config.layer.status = 'draft';
		this.Base_layers.render();
	}

	mouseup_action(e, index) { if(this.blockedStroke||this.strokePending)return;
		var mouse = this.get_mouse_info(e);
		if (mouse.click_valid == false) {
			config.layer.status = null;
			return;
		}

		config.layer.status = null;

		this.check_dimensions();
		this.Base_layers.render();
	}

	render(ctx, layer) {
		if (layer.data.length == 0)
			return;

		var params = layer.params;
		var size = params.size;

		//set styles
		ctx.save();
		ctx.fillStyle = layer.color;
		ctx.strokeStyle = layer.color;
		ctx.lineWidth = params.size;
		ctx.lineCap = 'round';
		ctx.lineJoin = 'round';

		ctx.translate(layer.x, layer.y);

		var data = layer.data;

		//check for legacy format
		data = this.check_legacy_format(data);

		var n = data.length;
		for (var k = 0; k < n; k++) {
			var group_data = data[k]; //data from mouse down till mouse release
			var group_n = group_data.length;

			if (params.pressure == false) {
				//stabilized lines method does not support multiple line sizes
				this.render_stabilized(ctx, group_data);
			}
			else {
				if (group_data[0]) {
					ctx.beginPath();
					ctx.moveTo(group_data[0][0], group_data[0][1]);
					for (var i = 1; i < group_n; i++) {
						if (group_data[i] === null) {
							//break
							ctx.beginPath();
						}
						else {
							//line

							ctx.lineWidth = group_data[i][2];

							if (group_data[i - 1] == null && group_data[i + 1] == null) {
								//exception - point
								ctx.arc(group_data[i][0], group_data[i][1], size / 2, 0, 2 * Math.PI, false);
								ctx.fill();
							}
							else if (group_data[i - 1] != null) {
								//lines
								ctx.lineWidth = group_data[i][2];
								ctx.beginPath();
								ctx.moveTo(group_data[i - 1][0], group_data[i - 1][1]);
								ctx.lineTo(group_data[i][0], group_data[i][1]);
								ctx.stroke();
							}
						}
					}
					if (group_data[1] == null) {
						//point
						ctx.beginPath();
						ctx.arc(group_data[0][0], group_data[0][1], size / 2, 0, 2 * Math.PI, false);
						ctx.fill();
					}
				}
			}
		}

		ctx.translate(-layer.x, -layer.y);
		ctx.restore();
	}

	// Dango renders the sampled stroke as round connected segments.
	render_stabilized(ctx, points) {
      if(!points.length)return;
      ctx.lineJoin='round';ctx.lineCap='round';ctx.beginPath();ctx.moveTo(points[0][0],points[0][1]);
      if(points.length===1){ctx.arc(points[0][0],points[0][1],(points[0][2]||ctx.lineWidth)/2,0,Math.PI*2);ctx.fill();return;}
      for(const point of points.slice(1))ctx.lineTo(point[0],point[1]);ctx.stroke();
    }

	check_legacy_format(data) {
		//check for legacy format
		if(data.length > 0 && typeof data[0][0] == "number"){
			//convert
			var legacy = JSON.parse(JSON.stringify(data));
			data = [];
			data.push([]);
			var group_index = 0;
			for(var i in legacy){
				if(legacy[i] === null){
					data.push([]);
					group_index++;
				}
				else {
					data[group_index].push([legacy[i][0], legacy[i][1], legacy[i][2]]);
				}
			}
		}

		return data;
	}

	/**
	 * recalculate layer x, y, width and height values.
	 */
	check_dimensions() {
		var data = JSON.parse(JSON.stringify(config.layer.data)); // Deep copy for history
		this.check_legacy_format(data);

		if(config.layer.data.length == 0 || data[0].length == 0)
			return;

		//find bounds
		var min_x = data[0][0][0];
		var min_y = data[0][0][1];
		var max_x = data[0][0][0];
		var max_y = data[0][0][1];

		var n = data.length;
		for (var k = 0; k < n; k++) {
			var group_data = data[k];
			var group_n = group_data.length;

			for (var i = 1; i < group_n; i++) {
				min_x = Math.min(min_x, group_data[i][0]);
				min_y = Math.min(min_y, group_data[i][1]);
				max_x = Math.max(max_x, group_data[i][0]);
				max_y = Math.max(max_y, group_data[i][1]);
			}
		}

		//move current data
		for (var k = 0; k < n; k++) {
			var group_data = data[k];
			var group_n = group_data.length;

			for (var i = 0; i < group_n; i++) {
				group_data[i][0] = group_data[i][0] - min_x;
				group_data[i][1] = group_data[i][1] - min_y;
			}
		}

		//change layers bounds
		app.State.do_action(
			new app.Actions.Update_layer_action(config.layer.id, {
				x: config.layer.x + min_x,
				y: config.layer.y + min_y,
				width: Math.max(1, max_x - min_x),
				height: Math.max(1, max_y - min_y),
				data
			}),
			{
				merge_with_history: ['new_brush_layer', 'update_brush_layer']
			}
		);
	}

}

export default Brush_class;
