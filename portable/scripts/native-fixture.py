import os
import sys
import gi

gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, GLib

command = sys.argv[1]
window = Gtk.Window(title='Dictate native check')
box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
first = Gtk.Entry()
first.set_text('Before ')
second = Gtk.Entry()
box.pack_start(first, True, True, 8)
box.pack_start(second, True, True, 8)
window.add(box)
window.show_all()
first.grab_focus()
first.set_position(-1)

def poll():
    if os.path.exists(command):
        with open(command) as source:
            action = source.read()
        if action == 'first':
            window.present()
            first.grab_focus()
            first.set_position(-1)
        elif action == 'second':
            second.grab_focus()
        elif action == 'edit':
            first.set_text(first.get_text() + ' changed')
            first.set_position(-1)
        os.unlink(command)
    return True

GLib.timeout_add(20, poll)
Gtk.main()
