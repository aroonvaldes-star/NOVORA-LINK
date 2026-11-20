"""
Ventana principal de NOVORA.
"""

import tkinter as tk


class NovoraApp:

    def __init__(self):

        self.window = tk.Tk()

        self.window.title("NOVORA V1")

        self.window.geometry("900x600")

        self.window.minsize(900, 600)

        self.window.configure(bg="#111111")

    def run(self):

        self.window.mainloop()