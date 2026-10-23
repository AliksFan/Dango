// Adapted from miniPaint (MIT); color conversions and throttle replaced by Dango.
import config from "../config";


class Helper_class {

	constructor() {
		this.time = null;
	}

	get_url_parameters() {
		var queryDict = {};
		location.search.substr(1).split("&").forEach(
			function (item) {
				queryDict[item.split("=")[0]] = item.split("=")[1];
			}
		);

		return queryDict;
	}

	
	timer_start() {
		this.time = Date.now();
	}

	
	timer_end(name, echo) {
		var text = (Math.round(Date.now() - this.time) / 1000) + " s";
		if (echo != undefined && echo === false)
			return text;
		if (name != undefined)
			text += ' (' + name + ')';
		console.log(text);
	}

	//format time
	format_time(datetime) {
		return new Date(datetime).toJSON().slice(0, 19).replace(/T/g, ' ');
	}

	
	strpos(haystack, needle, offset = 0) {
		var i = (haystack + '').indexOf(needle, (offset || 0));
		return i === -1 ? false : i;
	}

	
	getCookie(name) { return this.constructor.memory?.[name] ?? null; }

	
	setCookie(name,value) { (this.constructor.memory ??= {})[name]=value; }

	_getCookie(NameOfCookie) {
		if (document.cookie.length > 0) {
			var begin = document.cookie.indexOf(NameOfCookie + "=");
			if (begin != -1) {
				begin += NameOfCookie.length + 1;
				var end = document.cookie.indexOf(";", begin);
				if (end == -1)
					end = document.cookie.length;
				return document.cookie.substring(begin, end);
			}
		}
		return '';
	}

	_setCookie(NameOfCookie, value, expire_days) {
		if (expire_days == undefined)
			expire_days = 180;
		var ExpireDate = new Date();
		ExpireDate.setTime(ExpireDate.getTime() + (expire_days * 24 * 3600 * 1000));
		document.cookie = NameOfCookie + "=" + value +
			((expire_days == null) ? "" : "; expires=" + ExpireDate.toGMTString());
	}

	delCookie(NameOfCookie) {
		if (this.getCookie(NameOfCookie)) {
			document.cookie = NameOfCookie + "=" +
				"; expires=Thu, 01-Jan-70 00:00:01 GMT";
		}
	}

	getRandomInt(min, max) {
		return Math.floor(Math.random() * (max - min + 1)) + min;
	}

	font_pixel_to_height(px) {
		return Math.round(px * 0.75);
	}

	hex(x) {
		x = parseInt(x);
		return ("0" + x.toString(16)).slice(-2);
	}

	hex_set_hsl(hex, newHsl) {
		const rgb = this.hexToRgb(hex);
		const hsl = this.rgbToHsl(rgb.r, rgb.g, rgb.b);
		if ('h' in newHsl) {
			hsl.h = newHsl.h;
		}
		if ('s' in newHsl) {
			hsl.s = newHsl.s;
		}
		if ('l' in newHsl) {
			hsl.l = newHsl.l;
		}
		return this.hslToHex(hsl.h, hsl.s, hsl.l);
	}

	rgbToHex(r, g, b) {
		if (r > 255 || g > 255 || b > 255)
			throw "Invalid color component";
		var tmp = ((r << 16) | (g << 8) | b).toString(16);

		return "#" + ("000000" + tmp).slice(-6);
	}

	hexToRgb(hex) {
		if (hex[0] == "#")
			hex = hex.substr(1);
		if (hex.length == 3) {
			var temp = hex;
			hex = '';
			temp = /^([a-f0-9])([a-f0-9])([a-f0-9])$/i.exec(temp).slice(1);
			for (var i = 0; i < 3; i++)
				hex += temp[i] + temp[i];
		}
		var triplets = /^([a-f0-9]{2})([a-f0-9]{2})([a-f0-9]{2})/i.exec(hex).slice(1);
		return {
			r: parseInt(triplets[0], 16),
			g: parseInt(triplets[1], 16),
			b: parseInt(triplets[2], 16),
			a: 255
		};
	}

	hslToHex(h, s, l) {
		const rgb = this.hslToRgb(h, s, l);
		return this.rgbToHex(rgb.r, rgb.g, rgb.b);
	}

	hsvToHex(h, s, v) {
		const rgb = this.hsvToRgb(h, s, v);
		return this.rgbToHex(rgb.r, rgb.g, rgb.b);
	}

	hueToRgb(p, q, t) {
		if (t < 0)
			t += 1;
		if (t > 1)
			t -= 1;
		if (t < 1 / 6)
			return p + (q - p) * 6 * t;
		if (t < 1 / 2)
			return q;
		if (t < 2 / 3)
			return p + (q - p) * (2 / 3 - t) * 6;
		return p;
	}

	
	hslToRgb(h,s,l) { const c=(1-Math.abs(2*l-1))*s; const rgb=this.hsvToRgb(h,c/(l+c/2)||0,l+c/2); return {r:Math.round(rgb.r),g:Math.round(rgb.g),b:Math.round(rgb.b)}; }

	
	rgbToHsl(r,g,b) { const q=this.rgbToHsv(r,g,b), l=q.v*(1-q.s/2); return {h:q.h,s:l===0||l===1?0:(q.v-l)/Math.min(l,1-l),l}; }

	
	rgbToHsv(r,g,b) { const v=Math.max(r,g,b), d=v-Math.min(r,g,b); let h=0; if(d) h=(v===r?(g-b)/d:v===g?2+(b-r)/d:4+(r-g)/d)/6; return {h:(h+1)%1,s:v?d/v:0,v:v/255}; }

	
	hsvToRgb(h,s,v) { const channel=n=>{const k=(n+h*6)%6;return 255*v*(1-s*Math.max(0,Math.min(k,4-k,1)));}; return {r:channel(5),g:channel(3),b:channel(1)}; }

	
	hsvToHsl(h, s, v) {
		return {
			h,
			s: s * v / Math.max(0.00000001, ((h = (2 - s) * v) < 1 ? h : 2 - h)), 
			l: h / 2
		};
	}

	
	hslToHsv(h, s, l) {
		s *= l < .5 ? l : 1 - l;
		return {
			h,
			s: 2 * s / Math.max(0.00000001, (l + s)),
			v: l + s
		};
	}

	remove_selection() {
		if (window.getSelection) {
			if (window.getSelection().empty) // Chrome
				window.getSelection().empty();
			else if (window.getSelection().removeAllRanges) // Firefox
				window.getSelection().removeAllRanges();
		}
		else if (document.selection) // IE?
			document.selection.empty();
	}

	darkenColor(color, delta) { const value=color.replace(/^#/,''); return '#'+[0,2,4].map(i=>Math.max(0,Math.min(255,parseInt(value.slice(i,i+2),16)+delta)).toString(16).padStart(2,'0')).join(''); }

	
	number_format(n, maximumFractionDigits) {
		let x = parseFloat(n);
		var number = x.toLocaleString('us', {minimumFractionDigits: 0, maximumFractionDigits: maximumFractionDigits});
		number = number.replaceAll(',', '');
		number = parseFloat(number);

		return number;
	}

	check_input_color_support() {
		var i = document.createElement("input");
		i.setAttribute("type", "color");
		return i.type !== "text";
	}

	b64toBlob(b64Data, contentType, sliceSize) {
		contentType = contentType || '';
		sliceSize = sliceSize || 512;

		var byteCharacters = atob(b64Data);
		var byteArrays = [];

		for (var offset = 0; offset < byteCharacters.length; offset += sliceSize) {
			var slice = byteCharacters.slice(offset, offset + sliceSize);

			var byteNumbers = new Array(slice.length);
			for (var i = 0; i < slice.length; i++) {
				byteNumbers[i] = slice.charCodeAt(i);
			}

			var byteArray = new Uint8Array(byteNumbers);

			byteArrays.push(byteArray);
		}

		var blob = new Blob(byteArrays, {type: contentType});
		return blob;
	}

	escapeHtml(text) {
		return text
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;")
			.replace(/"/g, "&quot;")
			.replace(/'/g, "&#039;");
	}

	isNumeric(n) {
		return !isNaN(parseFloat(n)) && isFinite(n);
	}

	ucfirst(string) {
		return string.charAt(0).toUpperCase() + string.slice(1);
	}

	
	change_canvas_size(canvas, width, height, offset_x, offset_y) {
		if (offset_x == undefined)
			offset_x = 0;
		if (offset_y == undefined)
			offset_y = 0;

		//copy data;
		var tmp = document.createElement('canvas');
		var ctx = tmp.getContext("2d");
		tmp.width = canvas.width;
		tmp.height = canvas.height;
		ctx.drawImage(canvas, 0, 0);

		canvas.width = Math.max(1, width);
		canvas.height = Math.max(1, height);

		//restore image
		canvas.getContext("2d").drawImage(tmp, -offset_x, -offset_y);
	}

	image_round(ctx_main, mouse_x, mouse_y, size_w, size_h, img_data, anti_aliasing = false) {
		//create tmp canvas
		var canvasTmp = document.createElement('canvas');
		canvasTmp.width = size_w;
		canvasTmp.height = size_h;

		var size_half_w = Math.round(size_w / 2);
		var size_half_h = Math.round(size_h / 2);
		var ctx = canvasTmp.getContext("2d");
		var width = canvasTmp.width;
		var height = canvasTmp.height;
		var xx = mouse_x - size_half_w;
		var yy = mouse_y - size_half_h;

		ctx.clearRect(0, 0, width, height);
		ctx.save();
		//draw main data
		ctx.putImageData(img_data, 0, 0);
		ctx.globalCompositeOperation = 'destination-in';

		//create form
		var gradient = ctx.createRadialGradient(size_half_w, size_half_h, 0, size_half_w, size_half_h, size_half_w);
		gradient.addColorStop(0, '#ffffff');
		if (anti_aliasing == true)
			gradient.addColorStop(0.8, '#ffffff');
		else
			gradient.addColorStop(0.99, '#ffffff');
		gradient.addColorStop(1, 'rgba(255,255,255,0');
		ctx.fillStyle = gradient;

		ctx.beginPath();
		ctx.ellipse(size_half_w, size_half_h, size_w * 2, size_h * 2, 0, 0, 2 * Math.PI);
		ctx.fill();
		ctx_main.drawImage(canvasTmp, 0, 0, size_w, size_h, xx, yy, size_w, size_h);
		//reset
		ctx.restore();
		ctx.clearRect(0, 0, width, height);
	}
	
	is_input(element) {
		if (!element) {
			return false;
		}
		if (element.type == 'text' || element.tagName == 'INPUT' || element.type == 'textarea') {
			return true;
		} else {
			return element.closest('.ui_color_picker_gradient, .ui_number_input, .ui_range, .ui_swatches') != null;
		}
	}

	//if IE 11 or Edge
	is_edge_or_ie() {
		//ie11
		if( !(window.ActiveXObject) && "ActiveXObject" in window )
			return true;
		//edge
		if( navigator.userAgent.indexOf('Edge/') != -1 )
			return true;
		return false;
	}

	throttle(fn, wait, options={}) { let next=0,timer=null; return function(...args) { const now=Date.now(); if(now>=next && options.leading!==false) { next=now+wait; return fn.apply(this,args); } if(options.trailing!==false) { clearTimeout(timer); timer=setTimeout(()=>{next=Date.now()+wait;fn.apply(this,args);},Math.max(0,next-now)||wait); } }; };

	
	draw_special_line(ctx, start_x, start_y, end_x, end_y){
		const wholeLineWidth = 2 / config.ZOOM;
		const halfLineWidth = wholeLineWidth / 2;

		ctx.lineWidth = wholeLineWidth;
		ctx.strokeStyle = 'rgb(255, 255, 255)';
		ctx.beginPath();
		ctx.moveTo(start_x - halfLineWidth, start_y);
		ctx.lineTo(end_x - halfLineWidth, end_y);
		ctx.stroke();

		ctx.lineWidth = halfLineWidth;
		ctx.strokeStyle = 'rgb(0, 0, 0)';
		ctx.beginPath();
		ctx.moveTo(start_x - halfLineWidth, start_y);
		ctx.lineTo(end_x - halfLineWidth, end_y);
		ctx.stroke();
	}

	
	draw_control_point(ctx, x, y) {
		var dx = 0;
		var dy = 0;
		var block_size = 12 / config.ZOOM;
		const wholeLineWidth = 2 / config.ZOOM;

		ctx.strokeStyle = "#000000";
		ctx.fillStyle = "#ffffff";
		ctx.lineWidth = wholeLineWidth;

		//create path
		const circle = new Path2D();
		circle.arc(x + dx * block_size, y + dy * block_size, block_size / 2, 0, 2 * Math.PI);

		//draw
		ctx.fill(circle);
		ctx.stroke(circle);

		return circle;
	}

	
	get_user_unit(data, type, resolution){
		data = parseFloat(data);

		if(type == 'pixels'){
			//no conversion
			return parseInt(data);
		}
		else if(type == 'inches'){
			return this.number_format(data / resolution, 3);
		}
		else if(type == 'centimeters'){
			return this.number_format(data / resolution * 2.54, 3);
		}
		else if(type == 'millimetres'){
			return this.number_format(data / resolution * 25.4, 3);
		}
	}

	
	get_internal_unit(data, type, resolution){
		data = parseFloat(data);

		if(type == 'pixels'){
			//no conversion
			return parseInt(data);
		}
		else if(type == 'inches'){
			return Math.ceil(data * resolution);
		}
		else if(type == 'centimeters'){
			return Math.ceil(data * resolution / 2.54);
		}
		else if(type == 'millimetres'){
			return Math.ceil(data * resolution / 25.4);
		}
	}

}
export default Helper_class;