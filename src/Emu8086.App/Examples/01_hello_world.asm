; Hello World: print a string with DOS function 09h.
#make_COM#
org 100h

    mov dx, offset msg      ; DS:DX -> '$'-terminated string
    mov ah, 09h
    int 21h
    ret                     ; a COM program can end with RET

msg db 'Hello, World!$'
